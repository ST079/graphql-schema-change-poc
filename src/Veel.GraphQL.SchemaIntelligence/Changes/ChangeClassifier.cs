namespace Veel.GraphQL.SchemaIntelligence.Changes;

/// <summary>
/// Assigns a severity to each schema change, based only on the change itself.
/// Says nothing about which clients are affected; that is decided by impact analysis.
/// Rules are deliberately simple: any type change on an existing element is Breaking,
/// with no attempt at finer GraphQL type-compatibility analysis.
/// </summary>
public sealed class ChangeClassifier
{
    /// <exception cref="NotSupportedException">The change type has no classification rule.</exception>
    public ClassifiedSchemaChange Classify(SchemaChange change)
    {
        var (severity, reason) = change.ChangeType switch
        {
            ChangeType.TypeAdded => (ChangeSeverity.Info,
                "A new GraphQL type was added. Existing clients are not required to use it."),
            ChangeType.TypeRemoved => (ChangeSeverity.Breaking,
                "An existing GraphQL type was removed and clients using it may fail."),
            ChangeType.TypeKindChanged => (ChangeSeverity.Breaking,
                "The kind of an existing GraphQL type changed and clients relying on its previous shape may fail."),

            ChangeType.FieldAdded => (ChangeSeverity.Info,
                "A new field was added. Existing clients can continue using the previous schema."),
            ChangeType.FieldRemoved => (ChangeSeverity.Breaking,
                "An existing field was removed and clients using it may fail."),
            ChangeType.FieldTypeChanged => (ChangeSeverity.Breaking,
                "The type of an existing field changed and clients expecting the previous type may fail."),

            ChangeType.ArgumentAdded when IsRequiredWithoutDefault(change) => (ChangeSeverity.Breaking,
                "A required argument without a default value was added. Existing operations do not supply it and will fail."),
            ChangeType.ArgumentAdded => (ChangeSeverity.Info,
                "An optional argument was added. Existing operations can continue without it."),
            ChangeType.ArgumentRemoved => (ChangeSeverity.Breaking,
                "An existing argument was removed and operations passing it may fail."),
            ChangeType.ArgumentTypeChanged => (ChangeSeverity.Breaking,
                "The type of an existing argument changed and operations passing the previous type may fail."),

            ChangeType.InputFieldAdded when IsRequiredWithoutDefault(change) => (ChangeSeverity.Breaking,
                "A required input field without a default value was added. Existing operations do not supply it and will fail."),
            ChangeType.InputFieldAdded => (ChangeSeverity.Info,
                "An optional input field was added. Existing operations can continue without it."),
            ChangeType.InputFieldRemoved => (ChangeSeverity.Breaking,
                "An existing input field was removed and operations sending it may fail."),
            ChangeType.InputFieldTypeChanged => (ChangeSeverity.Breaking,
                "The type of an existing input field changed and operations sending the previous type may fail."),

            ChangeType.EnumValueAdded => (ChangeSeverity.Info,
                "A new enum value was added."),
            ChangeType.EnumValueRemoved => (ChangeSeverity.Breaking,
                "An existing enum value was removed and clients using that value may fail."),

            ChangeType.InterfaceAdded => (ChangeSeverity.Info,
                "The type now implements an additional interface. Existing clients are not affected."),
            ChangeType.InterfaceRemoved => (ChangeSeverity.Breaking,
                "The type no longer implements an interface and fragments on that interface may stop matching."),

            ChangeType.UnionMemberAdded => (ChangeSeverity.Info,
                "A new member type was added to the union."),
            ChangeType.UnionMemberRemoved => (ChangeSeverity.Breaking,
                "A member type was removed from the union and fragments on that type may stop matching."),

            ChangeType.DeprecationAdded => (ChangeSeverity.Warning,
                "An existing element was deprecated. It still works, but clients should migrate away from it."),
            ChangeType.DeprecationRemoved => (ChangeSeverity.Info,
                "An element is no longer deprecated."),

            _ => throw new NotSupportedException($"Unsupported schema change type: {change.ChangeType}"),
        };

        return new ClassifiedSchemaChange(change, severity, reason);
    }

    /// <summary>Classifies each change, preserving the input order.</summary>
    public IReadOnlyList<ClassifiedSchemaChange> Classify(IEnumerable<SchemaChange> changes) =>
        changes.Select(Classify).ToList();

    // For an added argument or input field, NewValue holds its default value (null when none).
    private static bool IsRequiredWithoutDefault(SchemaChange change) =>
        change.NewType is { IsNonNull: true } && change.NewValue is null;
}
