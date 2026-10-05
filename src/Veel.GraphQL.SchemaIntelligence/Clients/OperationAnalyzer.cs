using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Exceptions;
using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Clients;

/// <summary>
/// Loads client GraphQL operations and finds which of them use a changed schema element.
/// Works the same for every client; nothing here is Android- or frontend-specific.
/// </summary>
/// <remarks>
/// Operation selections are resolved against a schema to turn paths such as <c>campaign.videoUrl</c>
/// into schema coordinates such as <c>Campaign.videoUrl</c>. Pass the schema the clients are
/// currently built against, i.e. the old schema.
/// </remarks>
public sealed class OperationAnalyzer
{
    private static readonly string[] FilePatterns = ["*.graphql", "*.gql"];

    /// <summary>Recursively loads every operation in the client's directory.</summary>
    /// <remarks>Fragments may be defined in any file of the same client.</remarks>
    public IReadOnlyList<GraphQLOperation> LoadOperations(ClientDefinition client)
    {
        if (!Directory.Exists(client.OperationsDirectory))
        {
            throw new OperationLoadException($"{client.Name} GraphQL directory does not exist: {client.OperationsDirectory}");
        }

        var files = FilePatterns
            .SelectMany(pattern => Directory.EnumerateFiles(client.OperationsDirectory, pattern, SearchOption.AllDirectories))
            .Distinct()
            .Order(StringComparer.Ordinal);

        return ParseDocuments(client.Name, files.Select(path => (path, File.ReadAllText(path))));
    }

    /// <summary>Parses the operations in a single document.</summary>
    public IReadOnlyList<GraphQLOperation> ParseOperations(string clientName, string document, string filePath = "<inline>") =>
        ParseDocuments(clientName, [(filePath, document)]);

    /// <summary>
    /// Every field the operation selects, with fragments expanded, resolved to schema coordinates.
    /// Fields whose parent type cannot be determined (e.g. below a field missing from the schema) are skipped.
    /// </summary>
    public IReadOnlyList<GraphQLFieldReference> GetFieldReferences(GraphQLOperation operation, GraphQLSchema schema)
    {
        var rootType = operation.OperationType switch
        {
            GraphQLOperationType.Query => schema.QueryTypeName,
            GraphQLOperationType.Mutation => schema.MutationTypeName,
            GraphQLOperationType.Subscription => schema.SubscriptionTypeName,
            _ => null,
        };

        var references = new List<GraphQLFieldReference>();
        CollectReferences(operation.Selections, rootType, path: "", fragment: null, operation, schema, [], references);
        return references;
    }

    /// <summary>
    /// The operations that use the element affected by <paramref name="change"/>,
    /// ordered by client, operation name and file.
    /// </summary>
    /// <param name="schema">The schema the operations are written against (the old schema).</param>
    /// <exception cref="NotSupportedException">The change type has no matching rule.</exception>
    public IReadOnlyList<ClientOperationReference> FindAffectedOperations(
        ClassifiedSchemaChange change,
        IEnumerable<GraphQLOperation> operations,
        GraphQLSchema schema)
    {
        var results = new List<ClientOperationReference>();
        foreach (var operation in operations)
        {
            var usages = FindUsages(change.Change, operation, GetFieldReferences(operation, schema), schema);
            if (usages.Count > 0)
            {
                results.Add(new ClientOperationReference(
                    operation.ClientName,
                    operation.Name,
                    operation.OperationType,
                    operation.FilePath,
                    change.Change.TypeName,
                    change.Change.FieldName,
                    usages,
                    change));
            }
        }

        return results
            .OrderBy(r => r.ClientName, StringComparer.Ordinal)
            .ThenBy(r => r.OperationName, StringComparer.Ordinal)
            .ThenBy(r => r.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Matching changes to usages ------------------------------------------------------------

    private static IReadOnlyList<string> FindUsages(
        SchemaChange change, GraphQLOperation operation, IReadOnlyList<GraphQLFieldReference> references, GraphQLSchema schema)
    {
        var typeName = change.TypeName;

        IEnumerable<GraphQLFieldReference> FieldReferences() =>
            references.Where(r => r.TypeName == typeName && r.FieldName == change.FieldName);

        IEnumerable<string> ArgumentUsages() =>
            FieldReferences().Where(r => r.Arguments.ContainsKey(change.ArgumentName!)).Select(r => $"{Describe(r)} ({change.ArgumentName}:)");

        // A type is used if a field is selected on it, returns it, or takes it as an argument, or a variable has it.
        IEnumerable<string> TypeUsages() =>
            references
                .Where(r => r.TypeName == typeName || r.ReturnType == typeName || r.Arguments.Values.Contains(typeName))
                .Select(Describe)
                .Concat(operation.Variables.Where(v => v.Type.NamedType == typeName).Select(v => $"${v.Name}: {v.Type}"));

        var usages = change.ChangeType switch
        {
            // Field-level: operations selecting the field. A new argument concerns every call of the field.
            ChangeType.FieldAdded or ChangeType.FieldRemoved or ChangeType.FieldTypeChanged or ChangeType.ArgumentAdded =>
                FieldReferences().Select(Describe),

            ChangeType.ArgumentRemoved or ChangeType.ArgumentTypeChanged => ArgumentUsages(),

            ChangeType.DeprecationAdded or ChangeType.DeprecationRemoved when change.ArgumentName is not null => ArgumentUsages(),
            ChangeType.DeprecationAdded or ChangeType.DeprecationRemoved
                when schema.FindType(typeName)?.Kind is SchemaTypeKind.Object or SchemaTypeKind.Interface =>
                FieldReferences().Select(Describe),

            // Enum values, input fields, and deprecations on them: operations using the enum/input type at all.
            ChangeType.DeprecationAdded or ChangeType.DeprecationRemoved => TypeUsages(),
            ChangeType.EnumValueAdded or ChangeType.EnumValueRemoved => TypeUsages(),
            ChangeType.InputFieldAdded or ChangeType.InputFieldRemoved or ChangeType.InputFieldTypeChanged => TypeUsages(),

            // Type-level
            ChangeType.TypeAdded or ChangeType.TypeRemoved or ChangeType.TypeKindChanged
                or ChangeType.InterfaceAdded or ChangeType.InterfaceRemoved
                or ChangeType.UnionMemberAdded or ChangeType.UnionMemberRemoved => TypeUsages(),

            _ => throw new NotSupportedException($"Unsupported schema change type: {change.ChangeType}"),
        };

        return usages.Distinct().ToList();
    }

    private static string Describe(GraphQLFieldReference reference) =>
        reference.Path
        + (reference.Alias is null ? "" : $" (alias {reference.Alias})")
        + (reference.Fragment is null ? "" : $" (via fragment {reference.Fragment})");

    // ---- Resolving selections to field references -----------------------------------------------

    private static void CollectReferences(
        IReadOnlyList<Selection> selections,
        string? parentType,
        string path,
        string? fragment,
        GraphQLOperation operation,
        GraphQLSchema schema,
        HashSet<string> activeFragments,
        List<GraphQLFieldReference> references)
    {
        if (parentType is null)
        {
            return;
        }

        foreach (var selection in selections)
        {
            switch (selection)
            {
                case FieldSelection field when field.Name.StartsWith("__", StringComparison.Ordinal):
                    // Introspection meta-fields (__typename, ...) are not part of the schema contract.
                    break;

                case FieldSelection field:
                    var fieldPath = path.Length == 0 ? field.Name : $"{path}.{field.Name}";
                    var schemaField = schema.FindType(parentType)?.FindField(field.Name);
                    var arguments = field.Arguments.ToDictionary(
                        a => a,
                        a => schemaField?.FindArgument(a)?.Type.NamedType,
                        StringComparer.Ordinal);

                    references.Add(new GraphQLFieldReference(
                        parentType, field.Name, fieldPath, field.Alias, schemaField?.Type.NamedType, arguments, fragment));

                    CollectReferences(field.Selections, schemaField?.Type.NamedType, fieldPath, fragment, operation, schema, activeFragments, references);
                    break;

                case InlineFragmentSelection inline:
                    CollectReferences(inline.Selections, inline.TypeCondition ?? parentType, path, fragment, operation, schema, activeFragments, references);
                    break;

                // activeFragments guards against cyclic spreads, which are invalid GraphQL but must not hang the tool.
                case FragmentSpreadSelection spread
                    when operation.Fragments.TryGetValue(spread.FragmentName, out var definition) && activeFragments.Add(definition.Name):
                    CollectReferences(definition.Selections, definition.TypeCondition, path, definition.Name, operation, schema, activeFragments, references);
                    activeFragments.Remove(definition.Name);
                    break;
            }
        }
    }

    // ---- Parsing documents --------------------------------------------------------------------

    private static IReadOnlyList<GraphQLOperation> ParseDocuments(string clientName, IEnumerable<(string Path, string Text)> documents)
    {
        var fragments = new Dictionary<string, FragmentDefinition>(StringComparer.Ordinal);
        var operationDefinitions = new List<(string Path, GraphQLOperationDefinition Definition)>();

        foreach (var (path, text) in documents)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var definition in Parse(path, text).Definitions)
            {
                switch (definition)
                {
                    case GraphQLOperationDefinition operation:
                        operationDefinitions.Add((path, operation));
                        break;

                    case GraphQLFragmentDefinition fragment:
                        var name = fragment.FragmentName.Name.StringValue;
                        if (fragments.TryGetValue(name, out var existing))
                        {
                            throw new OperationLoadException(
                                $"Fragment '{name}' is defined more than once: {existing.FilePath} and {path}", path);
                        }

                        fragments[name] = new FragmentDefinition(
                            name, fragment.TypeCondition.Type.Name.StringValue, path, MapSelections(fragment.SelectionSet));
                        break;

                    // Type-system definitions (e.g. a schema file next to the operations) are not client operations.
                }
            }
        }

        return operationDefinitions.Select(item => BuildOperation(clientName, item.Path, item.Definition, fragments)).ToList();
    }

    private static GraphQLDocument Parse(string path, string text)
    {
        try
        {
            return Parser.Parse(text, new ParserOptions { Ignore = IgnoreOptions.Comments });
        }
        catch (GraphQLParserException ex)
        {
            throw new OperationLoadException(
                $"Failed to parse operation:{Environment.NewLine}{path}{Environment.NewLine}{ex.Description}{Environment.NewLine}Line: {ex.Location.Line}{Environment.NewLine}Column: {ex.Location.Column}",
                path,
                ex.Location.Line,
                ex.Location.Column);
        }
    }

    private static GraphQLOperation BuildOperation(
        string clientName, string path, GraphQLOperationDefinition definition, IReadOnlyDictionary<string, FragmentDefinition> allFragments)
    {
        var name = definition.Name?.StringValue ?? Path.GetFileNameWithoutExtension(path);
        var selections = MapSelections(definition.SelectionSet);

        var usedFragments = new Dictionary<string, FragmentDefinition>(StringComparer.Ordinal);
        CollectFragments(selections, allFragments, usedFragments, name, path);

        var variables = (definition.Variables?.Items ?? [])
            .Select(v => new VariableDefinition(v.Variable.Name.StringValue, SchemaLoader.MapTypeReference(v.Type)))
            .ToList();

        var operationType = definition.Operation switch
        {
            OperationType.Mutation => GraphQLOperationType.Mutation,
            OperationType.Subscription => GraphQLOperationType.Subscription,
            _ => GraphQLOperationType.Query,
        };

        return new GraphQLOperation(clientName, name, operationType, path, selections, variables, usedFragments);
    }

    /// <summary>Gathers the fragments an operation needs, following spreads inside fragments.</summary>
    private static void CollectFragments(
        IReadOnlyList<Selection> selections,
        IReadOnlyDictionary<string, FragmentDefinition> allFragments,
        Dictionary<string, FragmentDefinition> usedFragments,
        string operationName,
        string path)
    {
        foreach (var selection in selections)
        {
            switch (selection)
            {
                case FieldSelection field:
                    CollectFragments(field.Selections, allFragments, usedFragments, operationName, path);
                    break;

                case InlineFragmentSelection inline:
                    CollectFragments(inline.Selections, allFragments, usedFragments, operationName, path);
                    break;

                case FragmentSpreadSelection spread when !usedFragments.ContainsKey(spread.FragmentName):
                    if (!allFragments.TryGetValue(spread.FragmentName, out var fragment))
                    {
                        throw new OperationLoadException(
                            $"Operation '{operationName}' uses unknown fragment '{spread.FragmentName}': {path}", path);
                    }

                    usedFragments[fragment.Name] = fragment;
                    CollectFragments(fragment.Selections, allFragments, usedFragments, operationName, path);
                    break;
            }
        }
    }

    private static IReadOnlyList<Selection> MapSelections(GraphQLSelectionSet? selectionSet) =>
        (selectionSet?.Selections ?? []).Select(MapSelection).ToList();

    private static Selection MapSelection(ASTNode node) => node switch
    {
        GraphQLField field => new FieldSelection(
            field.Name.StringValue,
            field.Alias?.Name.StringValue,
            (field.Arguments?.Items ?? []).Select(a => a.Name.StringValue).ToList(),
            MapSelections(field.SelectionSet)),
        GraphQLInlineFragment inline => new InlineFragmentSelection(
            inline.TypeCondition?.Type.Name.StringValue,
            MapSelections(inline.SelectionSet)),
        GraphQLFragmentSpread spread => new FragmentSpreadSelection(spread.FragmentName.Name.StringValue),
        _ => throw new NotSupportedException($"Unknown selection '{node.Kind}'."),
    };
}
