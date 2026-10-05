using Veel.GraphQL.SchemaIntelligence.Changes;

namespace Veel.GraphQL.SchemaIntelligence.Schema;

/// <summary>
/// Compares two loaded schemas and reports structural differences.
/// It does not judge severity, and it never pairs a removal with an addition as a rename:
/// <c>videoUrl</c> → <c>video</c> is reported as one removal and one addition.
/// </summary>
public sealed class SchemaDiffer
{
    /// <returns>Changes in a deterministic order: type name, field, argument, then change type.</returns>
    public IReadOnlyList<SchemaChange> Compare(GraphQLSchema oldSchema, GraphQLSchema newSchema)
    {
        var changes = new List<SchemaChange>();

        Match(
            oldSchema.Types,
            newSchema.Types,
            t => t.Name,
            removed: type => changes.Add(Change(
                ChangeType.TypeRemoved, type.Name,
                $"Type '{type.Name}' ({type.Kind}) was removed.",
                oldValue: type.Kind.ToString())),
            added: type => changes.Add(Change(
                ChangeType.TypeAdded, type.Name,
                $"Type '{type.Name}' ({type.Kind}) was added.",
                newValue: type.Kind.ToString())),
            matched: (oldType, newType) => CompareType(oldType, newType, changes));

        changes.Sort(CompareForOrdering);
        return changes;
    }

    private static void CompareType(SchemaType oldType, SchemaType newType, List<SchemaChange> changes)
    {
        if (oldType.Kind != newType.Kind)
        {
            // Members of different kinds are not comparable (e.g. fields vs input fields), so stop here.
            changes.Add(Change(
                ChangeType.TypeKindChanged, oldType.Name,
                $"Type '{oldType.Name}' changed from {oldType.Kind} to {newType.Kind}.",
                oldValue: oldType.Kind.ToString(), newValue: newType.Kind.ToString()));
            return;
        }

        var typeName = oldType.Name;
        switch (oldType.Kind)
        {
            case SchemaTypeKind.Object or SchemaTypeKind.Interface:
                CompareFields(typeName, oldType.Fields, newType.Fields, changes);
                Match(
                    oldType.Interfaces, newType.Interfaces, i => i,
                    removed: i => changes.Add(Change(
                        ChangeType.InterfaceRemoved, typeName,
                        $"Type '{typeName}' no longer implements interface '{i}'.",
                        oldType: new NamedTypeReference(i))),
                    added: i => changes.Add(Change(
                        ChangeType.InterfaceAdded, typeName,
                        $"Type '{typeName}' now implements interface '{i}'.",
                        newType: new NamedTypeReference(i))));
                break;

            case SchemaTypeKind.InputObject:
                CompareInputValues(
                    typeName, field: null, oldType.InputFields, newType.InputFields,
                    ChangeType.InputFieldAdded, ChangeType.InputFieldRemoved, ChangeType.InputFieldTypeChanged, changes);
                break;

            case SchemaTypeKind.Enum:
                Match(
                    oldType.EnumValues, newType.EnumValues, v => v.Name,
                    removed: v => changes.Add(Change(
                        ChangeType.EnumValueRemoved, typeName,
                        $"Enum value '{typeName}.{v.Name}' was removed.",
                        field: v.Name)),
                    added: v => changes.Add(Change(
                        ChangeType.EnumValueAdded, typeName,
                        $"Enum value '{typeName}.{v.Name}' was added.",
                        field: v.Name)),
                    matched: (oldValue, newValue) => CompareDeprecation(
                        $"Enum value '{typeName}.{oldValue.Name}'", typeName, oldValue.Name, argument: null,
                        oldValue.IsDeprecated, oldValue.DeprecationReason, newValue.IsDeprecated, newValue.DeprecationReason, changes));
                break;

            case SchemaTypeKind.Union:
                Match(
                    oldType.PossibleTypes, newType.PossibleTypes, m => m,
                    removed: m => changes.Add(Change(
                        ChangeType.UnionMemberRemoved, typeName,
                        $"Type '{m}' was removed from union '{typeName}'.",
                        oldType: new NamedTypeReference(m))),
                    added: m => changes.Add(Change(
                        ChangeType.UnionMemberAdded, typeName,
                        $"Type '{m}' was added to union '{typeName}'.",
                        newType: new NamedTypeReference(m))));
                break;

            case SchemaTypeKind.Scalar:
                // A scalar has no structure beyond its name.
                break;
        }
    }

    private static void CompareFields(
        string typeName, IReadOnlyList<SchemaField> oldFields, IReadOnlyList<SchemaField> newFields, List<SchemaChange> changes)
    {
        Match(
            oldFields, newFields, f => f.Name,
            removed: field => changes.Add(Change(
                ChangeType.FieldRemoved, typeName,
                $"Field '{typeName}.{field.Name}' of type '{field.Type}' was removed.",
                field: field.Name, oldType: field.Type)),
            added: field => changes.Add(Change(
                ChangeType.FieldAdded, typeName,
                $"Field '{typeName}.{field.Name}' was added with type '{field.Type}'.",
                field: field.Name, newType: field.Type)),
            matched: (oldField, newField) =>
            {
                var path = $"{typeName}.{oldField.Name}";
                if (oldField.Type != newField.Type)
                {
                    changes.Add(Change(
                        ChangeType.FieldTypeChanged, typeName,
                        $"Field '{path}' changed type from '{oldField.Type}' to '{newField.Type}'.",
                        field: oldField.Name, oldType: oldField.Type, newType: newField.Type));
                }

                CompareDeprecation(
                    $"Field '{path}'", typeName, oldField.Name, argument: null,
                    oldField.IsDeprecated, oldField.DeprecationReason, newField.IsDeprecated, newField.DeprecationReason, changes);

                CompareInputValues(
                    typeName, oldField.Name, oldField.Arguments, newField.Arguments,
                    ChangeType.ArgumentAdded, ChangeType.ArgumentRemoved, ChangeType.ArgumentTypeChanged, changes);
            });
    }

    /// <summary>
    /// Compares either the arguments of a field (<paramref name="field"/> set)
    /// or the fields of an input object (<paramref name="field"/> null).
    /// </summary>
    private static void CompareInputValues(
        string typeName,
        string? field,
        IReadOnlyList<SchemaInputValue> oldValues,
        IReadOnlyList<SchemaInputValue> newValues,
        ChangeType addedType,
        ChangeType removedType,
        ChangeType typeChangedType,
        List<SchemaChange> changes)
    {
        var isArgument = field is not null;
        string Describe(SchemaInputValue value) => isArgument
            ? $"Argument '{typeName}.{field}({value.Name}:)'"
            : $"Input field '{typeName}.{value.Name}'";
        SchemaChange Create(ChangeType changeType, SchemaInputValue value, string description,
            TypeReference? oldType = null, TypeReference? newType = null, string? oldValue = null, string? newValue = null) =>
            Change(changeType, typeName, description,
                field: isArgument ? field : value.Name,
                argument: isArgument ? value.Name : null,
                oldType: oldType, newType: newType, oldValue: oldValue, newValue: newValue);

        // Added/removed input values carry their default value: whether a client must supply one depends on it.
        Match(
            oldValues, newValues, v => v.Name,
            removed: value => changes.Add(Create(
                removedType, value, $"{Describe(value)} of type '{value.Type}' was removed.",
                oldType: value.Type, oldValue: value.DefaultValue)),
            added: value => changes.Add(Create(
                addedType, value, $"{Describe(value)} was added with type '{value.Type}'.",
                newType: value.Type, newValue: value.DefaultValue)),
            matched: (oldValue, newValue) =>
            {
                if (oldValue.Type != newValue.Type)
                {
                    changes.Add(Create(
                        typeChangedType, oldValue,
                        $"{Describe(oldValue)} changed type from '{oldValue.Type}' to '{newValue.Type}'.",
                        oldType: oldValue.Type, newType: newValue.Type));
                }

                CompareDeprecation(
                    Describe(oldValue), typeName,
                    isArgument ? field : oldValue.Name,
                    isArgument ? oldValue.Name : null,
                    oldValue.IsDeprecated, oldValue.DeprecationReason, newValue.IsDeprecated, newValue.DeprecationReason, changes);
            });
    }

    private static void CompareDeprecation(
        string subject, string typeName, string? field, string? argument,
        bool wasDeprecated, string? oldReason, bool isDeprecated, string? newReason,
        List<SchemaChange> changes)
    {
        if (!wasDeprecated && isDeprecated)
        {
            changes.Add(Change(
                ChangeType.DeprecationAdded, typeName,
                $"{subject} was deprecated: {newReason}",
                field: field, argument: argument, newValue: newReason));
        }
        else if (wasDeprecated && !isDeprecated)
        {
            changes.Add(Change(
                ChangeType.DeprecationRemoved, typeName,
                $"{subject} is no longer deprecated.",
                field: field, argument: argument, oldValue: oldReason));
        }
    }

    /// <summary>Pairs items of two lists by name and dispatches removed, added and matched items.</summary>
    private static void Match<T>(
        IReadOnlyList<T> oldItems,
        IReadOnlyList<T> newItems,
        Func<T, string> getName,
        Action<T> removed,
        Action<T> added,
        Action<T, T>? matched = null)
    {
        var newByName = newItems.ToDictionary(getName, StringComparer.Ordinal);
        var oldNames = new HashSet<string>(oldItems.Select(getName), StringComparer.Ordinal);

        foreach (var oldItem in oldItems)
        {
            if (newByName.TryGetValue(getName(oldItem), out var newItem))
            {
                matched?.Invoke(oldItem, newItem);
            }
            else
            {
                removed(oldItem);
            }
        }

        foreach (var newItem in newItems.Where(i => !oldNames.Contains(getName(i))))
        {
            added(newItem);
        }
    }

    private static SchemaChange Change(
        ChangeType changeType,
        string typeName,
        string description,
        string? field = null,
        string? argument = null,
        TypeReference? oldType = null,
        TypeReference? newType = null,
        string? oldValue = null,
        string? newValue = null) =>
        new(changeType, typeName, field, argument, oldType, newType, oldValue, newValue, description);

    /// <summary>Location order of changes: type, field, argument, change type. Shared with impact analysis.</summary>
    internal static int CompareForOrdering(SchemaChange a, SchemaChange b)
    {
        // Null sorts first, so a type-level change precedes changes to its fields.
        var result = string.CompareOrdinal(a.TypeName, b.TypeName);
        if (result == 0) result = string.CompareOrdinal(a.FieldName, b.FieldName);
        if (result == 0) result = string.CompareOrdinal(a.ArgumentName, b.ArgumentName);
        if (result == 0) result = a.ChangeType.CompareTo(b.ChangeType);
        // Tie-breaker for changes that share a location, e.g. two interfaces added to one type.
        if (result == 0) result = string.CompareOrdinal(a.Description, b.Description);
        return result;
    }
}
