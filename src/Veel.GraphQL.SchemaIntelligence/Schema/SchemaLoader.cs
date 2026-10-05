using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Exceptions;
using GraphQLParser.Visitors;

namespace Veel.GraphQL.SchemaIntelligence.Schema;

/// <summary>
/// Loads a GraphQL SDL document into a validated <see cref="GraphQLSchema"/>.
/// Parsing and validation only; comparing schemas is the job of the differ.
/// </summary>
public static class SchemaLoader
{
    private const string DefaultDeprecationReason = "No longer supported";

    // Renders default values (e.g. `10`, `"x"`, `[A, B]`) back to GraphQL syntax for comparison and display.
    private static readonly SDLPrinter ValuePrinter = new();

    public static GraphQLSchema LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new SchemaLoadException(path, $"Schema file does not exist: {path}");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <param name="sdl">The schema definition language text.</param>
    /// <param name="source">File path or name used in error messages.</param>
    public static GraphQLSchema Parse(string sdl, string source = "<inline>")
    {
        if (string.IsNullOrWhiteSpace(sdl))
        {
            throw new SchemaLoadException(source, $"Schema is empty: {source}");
        }

        GraphQLDocument document;
        try
        {
            document = Parser.Parse(sdl, new ParserOptions { Ignore = IgnoreOptions.Comments });
        }
        catch (GraphQLParserException ex)
        {
            throw new SchemaLoadException(
                source,
                $"Schema is invalid GraphQL: {source}{Environment.NewLine}{ex.Description}{Environment.NewLine}Line: {ex.Location.Line}{Environment.NewLine}Column: {ex.Location.Column}",
                [ex.Description],
                ex.Location.Line,
                ex.Location.Column);
        }

        var errors = new List<string>();
        var schema = BuildSchema(document, errors);
        if (errors.Count == 0)
        {
            Validate(schema, errors);
        }

        if (errors.Count > 0)
        {
            var details = string.Join(Environment.NewLine, errors.Select(e => $"  - {e}"));
            throw new SchemaLoadException(source, $"Schema failed validation: {source}{Environment.NewLine}{details}", errors);
        }

        return schema;
    }

    private static GraphQLSchema BuildSchema(GraphQLDocument document, List<string> errors)
    {
        var types = new List<SchemaType>();
        var seenTypeNames = new HashSet<string>(StringComparer.Ordinal);
        string? queryTypeName = null, mutationTypeName = null, subscriptionTypeName = null;
        var hasSchemaDefinition = false;

        foreach (var definition in document.Definitions)
        {
            switch (definition)
            {
                // Must precede GraphQLTypeDefinition: the parser models directive definitions as a subtype of it.
                case GraphQLDirectiveDefinition:
                    // Custom directive definitions do not affect the schema shape we compare.
                    break;

                case GraphQLTypeDefinition typeDefinition:
                    var type = MapType(typeDefinition, errors);
                    if (!seenTypeNames.Add(type.Name))
                    {
                        errors.Add($"Type '{type.Name}' is defined more than once.");
                        continue;
                    }

                    types.Add(type);
                    break;

                case GraphQLSchemaDefinition schemaDefinition:
                    if (hasSchemaDefinition)
                    {
                        errors.Add("The schema definition ('schema { ... }') appears more than once.");
                    }

                    hasSchemaDefinition = true;
                    foreach (var operationType in schemaDefinition.OperationTypes)
                    {
                        var typeName = operationType.Type!.Name.StringValue;
                        switch (operationType.Operation)
                        {
                            case OperationType.Query: queryTypeName = typeName; break;
                            case OperationType.Mutation: mutationTypeName = typeName; break;
                            case OperationType.Subscription: subscriptionTypeName = typeName; break;
                        }
                    }

                    break;

                case GraphQLExecutableDefinition:
                    errors.Add("Schema contains an operation or fragment. Client operations belong in client directories, not in the schema file.");
                    break;

                default:
                    errors.Add($"'{definition.Kind}' is not supported yet. Provide the schema with extensions merged into the type definitions.");
                    break;
            }
        }

        // Without an explicit schema definition, the spec's conventional root type names apply.
        if (!hasSchemaDefinition)
        {
            queryTypeName = "Query";
            mutationTypeName = seenTypeNames.Contains("Mutation") ? "Mutation" : null;
            subscriptionTypeName = seenTypeNames.Contains("Subscription") ? "Subscription" : null;
        }

        if (queryTypeName is null)
        {
            errors.Add("The schema definition does not declare a query root type.");
            queryTypeName = "Query";
        }

        return new GraphQLSchema(types, queryTypeName, mutationTypeName, subscriptionTypeName);
    }

    private static SchemaType MapType(GraphQLTypeDefinition definition, List<string> errors)
    {
        var name = definition.Name.StringValue;
        var fields = new List<SchemaField>();
        var inputFields = new List<SchemaInputValue>();
        var enumValues = new List<SchemaEnumValue>();
        var interfaces = new List<string>();
        var possibleTypes = new List<string>();
        SchemaTypeKind kind;

        switch (definition)
        {
            case GraphQLObjectTypeDefinition objectType:
                kind = SchemaTypeKind.Object;
                fields.AddRange(MapFields(objectType.Fields));
                interfaces.AddRange(MapNames(objectType.Interfaces?.Items));
                break;

            case GraphQLInterfaceTypeDefinition interfaceType:
                kind = SchemaTypeKind.Interface;
                fields.AddRange(MapFields(interfaceType.Fields));
                interfaces.AddRange(MapNames(interfaceType.Interfaces?.Items));
                break;

            case GraphQLInputObjectTypeDefinition inputType:
                kind = SchemaTypeKind.InputObject;
                inputFields.AddRange((inputType.Fields?.Items ?? []).Select(MapInputValue));
                break;

            case GraphQLEnumTypeDefinition enumType:
                kind = SchemaTypeKind.Enum;
                enumValues.AddRange((enumType.Values?.Items ?? []).Select(value =>
                {
                    var (isDeprecated, reason) = ReadDeprecation(value.Directives);
                    return new SchemaEnumValue(value.Name.StringValue, isDeprecated, reason);
                }));
                break;

            case GraphQLUnionTypeDefinition unionType:
                kind = SchemaTypeKind.Union;
                possibleTypes.AddRange(MapNames(unionType.Types?.Items));
                break;

            case GraphQLScalarTypeDefinition:
                kind = SchemaTypeKind.Scalar;
                break;

            default:
                throw new NotSupportedException($"Unknown type definition '{definition.Kind}'.");
        }

        ReportDuplicates(fields.Select(f => f.Name), $"field of type '{name}'", errors);
        ReportDuplicates(inputFields.Select(f => f.Name), $"input field of type '{name}'", errors);
        ReportDuplicates(enumValues.Select(v => v.Name), $"value of enum '{name}'", errors);
        ReportDuplicates(interfaces, $"interface implemented by '{name}'", errors);
        ReportDuplicates(possibleTypes, $"member type of union '{name}'", errors);
        foreach (var field in fields)
        {
            ReportDuplicates(field.Arguments.Select(a => a.Name), $"argument of field '{name}.{field.Name}'", errors);
        }

        return new SchemaType(name, kind, fields, inputFields, enumValues, interfaces, possibleTypes);
    }

    private static IEnumerable<SchemaField> MapFields(GraphQLFieldsDefinition? fields) =>
        (fields?.Items ?? []).Select(field =>
        {
            var arguments = (field.Arguments?.Items ?? []).Select(MapInputValue).ToList();
            var (isDeprecated, reason) = ReadDeprecation(field.Directives);
            return new SchemaField(field.Name.StringValue, MapTypeReference(field.Type), arguments, isDeprecated, reason);
        });

    private static SchemaInputValue MapInputValue(GraphQLInputValueDefinition value)
    {
        var (isDeprecated, reason) = ReadDeprecation(value.Directives);
        return new SchemaInputValue(
            value.Name.StringValue,
            MapTypeReference(value.Type),
            value.DefaultValue is null ? null : ValuePrinter.Print(value.DefaultValue),
            isDeprecated,
            reason);
    }

    private static IEnumerable<string> MapNames(IEnumerable<GraphQLNamedType>? namedTypes) =>
        (namedTypes ?? []).Select(t => t.Name.StringValue);

    internal static TypeReference MapTypeReference(GraphQLType type) => type switch
    {
        GraphQLNamedType named => new NamedTypeReference(named.Name.StringValue),
        GraphQLListType list => new ListTypeReference(MapTypeReference(list.Type)),
        GraphQLNonNullType nonNull => new NonNullTypeReference(MapTypeReference(nonNull.Type)),
        _ => throw new NotSupportedException($"Unknown type reference '{type.Kind}'."),
    };

    private static (bool IsDeprecated, string? Reason) ReadDeprecation(GraphQLDirectives? directives)
    {
        var deprecated = directives?.Items.FirstOrDefault(d => d.Name.StringValue == "deprecated");
        if (deprecated is null)
        {
            return (false, null);
        }

        var reason = deprecated.Arguments?.Items.FirstOrDefault(a => a.Name.StringValue == "reason")?.Value;
        return (true, reason is GraphQLStringValue text ? text.Value.ToString() : DefaultDeprecationReason);
    }

    private static void ReportDuplicates(IEnumerable<string> names, string description, List<string> errors)
    {
        foreach (var duplicate in names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate {description}: '{duplicate.Key}'.");
        }
    }

    private static void Validate(GraphQLSchema schema, List<string> errors)
    {
        ValidateRootType(schema, "query", schema.QueryTypeName, errors);
        ValidateRootType(schema, "mutation", schema.MutationTypeName, errors);
        ValidateRootType(schema, "subscription", schema.SubscriptionTypeName, errors);

        foreach (var type in schema.Types)
        {
            switch (type.Kind)
            {
                case SchemaTypeKind.Object or SchemaTypeKind.Interface:
                    if (type.Fields.Count == 0)
                    {
                        errors.Add($"Type '{type.Name}' must define at least one field.");
                    }

                    foreach (var field in type.Fields)
                    {
                        var path = $"{type.Name}.{field.Name}";
                        ValidateTypeUsage(schema, field.Type, $"Field '{path}'", isInput: false, errors);
                        foreach (var argument in field.Arguments)
                        {
                            ValidateTypeUsage(schema, argument.Type, $"Argument '{path}({argument.Name})'", isInput: true, errors);
                        }
                    }

                    foreach (var interfaceName in type.Interfaces)
                    {
                        ValidateReferencedKind(schema, interfaceName, SchemaTypeKind.Interface, $"Type '{type.Name}' implements", errors);
                    }

                    break;

                case SchemaTypeKind.InputObject:
                    if (type.InputFields.Count == 0)
                    {
                        errors.Add($"Input type '{type.Name}' must define at least one field.");
                    }

                    foreach (var inputField in type.InputFields)
                    {
                        ValidateTypeUsage(schema, inputField.Type, $"Input field '{type.Name}.{inputField.Name}'", isInput: true, errors);
                    }

                    break;

                case SchemaTypeKind.Enum when type.EnumValues.Count == 0:
                    errors.Add($"Enum '{type.Name}' must define at least one value.");
                    break;

                case SchemaTypeKind.Union:
                    if (type.PossibleTypes.Count == 0)
                    {
                        errors.Add($"Union '{type.Name}' must include at least one member type.");
                    }

                    foreach (var member in type.PossibleTypes)
                    {
                        ValidateReferencedKind(schema, member, SchemaTypeKind.Object, $"Union '{type.Name}' includes", errors);
                    }

                    break;
            }
        }
    }

    private static void ValidateRootType(GraphQLSchema schema, string operation, string? typeName, List<string> errors)
    {
        if (typeName is null)
        {
            return;
        }

        var type = schema.FindType(typeName);
        if (type is null)
        {
            errors.Add($"The {operation} root type '{typeName}' is not defined.");
        }
        else if (type.Kind != SchemaTypeKind.Object)
        {
            errors.Add($"The {operation} root type '{typeName}' must be an object type, but is {type.Kind}.");
        }
    }

    private static void ValidateTypeUsage(GraphQLSchema schema, TypeReference reference, string owner, bool isInput, List<string> errors)
    {
        var name = reference.NamedType;
        if (GraphQLSchema.BuiltInScalars.Contains(name))
        {
            return;
        }

        var type = schema.FindType(name);
        if (type is null)
        {
            errors.Add($"{owner} references unknown type '{name}'.");
            return;
        }

        var isInputType = type.Kind is SchemaTypeKind.Scalar or SchemaTypeKind.Enum or SchemaTypeKind.InputObject;
        var isOutputType = type.Kind is not SchemaTypeKind.InputObject;
        if (isInput && !isInputType)
        {
            errors.Add($"{owner} must use an input type, but '{name}' is {type.Kind}.");
        }
        else if (!isInput && !isOutputType)
        {
            errors.Add($"{owner} must use an output type, but '{name}' is {type.Kind}.");
        }
    }

    private static void ValidateReferencedKind(GraphQLSchema schema, string name, SchemaTypeKind expected, string owner, List<string> errors)
    {
        var type = schema.FindType(name);
        if (type is null)
        {
            errors.Add($"{owner} unknown type '{name}'.");
        }
        else if (type.Kind != expected)
        {
            errors.Add($"{owner} '{name}', which is {type.Kind}, not {expected}.");
        }
    }
}
