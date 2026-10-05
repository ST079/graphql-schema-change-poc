using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Impact;

/// <summary>
/// Combines classified changes with the client operations that use them into per-client impacts
/// and deterministic recommendations. Severity is taken from classification as-is, never recalculated.
/// </summary>
public sealed class ImpactAnalyzer
{
    /// <param name="changes">All classified changes of one schema comparison.</param>
    /// <param name="operationReferences">Affected operations found for those changes.</param>
    /// <exception cref="ArgumentException">A reference points to a change that is not in <paramref name="changes"/>.</exception>
    /// <exception cref="InvalidOperationException">A change or reference lacks data its kind requires.</exception>
    public ImpactAnalysis Analyze(
        IEnumerable<ClassifiedSchemaChange> changes,
        IEnumerable<ClientOperationReference> operationReferences)
    {
        var changeList = changes.ToList();
        changeList.ForEach(Validate);

        var referencesByChange = changeList.Distinct().ToDictionary(c => c, _ => new List<ClientOperationReference>());
        foreach (var reference in operationReferences)
        {
            if (string.IsNullOrWhiteSpace(reference.ClientName) || string.IsNullOrWhiteSpace(reference.OperationName))
            {
                throw new InvalidOperationException($"Operation reference in '{reference.FilePath}' has no client or operation name.");
            }

            if (!referencesByChange.TryGetValue(reference.Change, out var list))
            {
                throw new ArgumentException(
                    $"Operation '{reference.ClientName}/{reference.OperationName}' references a change that is not part of the analysis: {reference.Change.Change.Description}",
                    nameof(operationReferences));
            }

            list.Add(reference);
        }

        var impacts = changeList
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.Change, Comparer<SchemaChange>.Create(SchemaDiffer.CompareForOrdering))
            .Select(change => AnalyzeChange(change, referencesByChange[change], changeList))
            .ToList();

        return new ImpactAnalysis(impacts, Summarize(impacts));
    }

    private static SchemaChangeImpact AnalyzeChange(
        ClassifiedSchemaChange change, List<ClientOperationReference> references, IReadOnlyList<ClassifiedSchemaChange> allChanges)
    {
        var possibleMigrations = FindPossibleMigrations(change.Change, allChanges);
        var recommendedAction = Recommend(change);

        var clientImpacts = references
            .GroupBy(r => r.ClientName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var operations = group
                    .OrderBy(r => r.OperationName, StringComparer.Ordinal)
                    .ThenBy(r => r.FilePath, StringComparer.Ordinal)
                    .ToList();
                var reason = $"{change.Change.Description} {operations.Count} {group.Key} operation(s) use it.";
                return new ClientImpact(group.Key, change, operations, reason, recommendedAction);
            })
            .ToList();

        return new SchemaChangeImpact(change, clientImpacts, recommendedAction, possibleMigrations);
    }

    private static ImpactSummary Summarize(IReadOnlyList<SchemaChangeImpact> impacts)
    {
        var clients = impacts
            .SelectMany(i => i.ClientImpacts)
            .GroupBy(c => c.ClientName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ClientImpactSummary(
                g.Key,
                g.SelectMany(c => c.AffectedOperations).Select(o => (o.OperationName, o.FilePath)).Distinct().Count(),
                g.Max(c => c.Severity)))
            .ToList();

        return new ImpactSummary(
            impacts.Count,
            impacts.Count(i => i.Severity == ChangeSeverity.Breaking),
            impacts.Count(i => i.Severity == ChangeSeverity.Warning),
            impacts.Count(i => i.Severity == ChangeSeverity.Info),
            clients);
    }

    // ---- Recommendations ---------------------------------------------------------------------

    /// <summary>
    /// The base action for a change. Possible migrations are kept as separate data
    /// (<see cref="SchemaChangeImpact.PossibleMigrations"/>) so each output can label them appropriately.
    /// </summary>
    private static string Recommend(ClassifiedSchemaChange classified)
    {
        var change = classified.Change;
        var path = change.Path;
        var isBreaking = classified.Severity == ChangeSeverity.Breaking;

        return change.ChangeType switch
        {
            ChangeType.TypeAdded =>
                "No action required for existing clients unless they need the new GraphQL type.",
            ChangeType.TypeRemoved =>
                "Remove usage of the deleted GraphQL type from affected client operations and client models.",
            ChangeType.TypeKindChanged =>
                $"Review selections and fragments on {path} in affected operations; it changed from {change.OldValue} to {change.NewValue}.",

            ChangeType.FieldAdded =>
                "No action required for existing clients unless they want to use the new field.",
            ChangeType.FieldRemoved =>
                $"Update affected client operations to stop requesting {path}.",
            ChangeType.FieldTypeChanged when IsNullabilityOnlyChange(change.OldType!, change.NewType!) =>
                "Review affected client operations and update generated/client models if required.",
            ChangeType.FieldTypeChanged =>
                "Update the affected client operation and corresponding client model to handle the new field type.",

            ChangeType.ArgumentAdded when isBreaking =>
                $"Pass the new required argument {path} in affected operations.",
            ChangeType.ArgumentAdded =>
                "No action required for existing clients; the new argument is optional.",
            ChangeType.ArgumentRemoved =>
                $"Stop passing {path} in affected operations.",
            ChangeType.ArgumentTypeChanged =>
                $"Update the value or variable passed to {path} to match the new type {change.NewType}.",

            ChangeType.InputFieldAdded when isBreaking =>
                $"Provide the new required input field {path} in affected operations.",
            ChangeType.InputFieldAdded =>
                "No action required for existing clients; the new input field is optional.",
            ChangeType.InputFieldRemoved =>
                $"Stop sending {path} in affected operations.",
            ChangeType.InputFieldTypeChanged =>
                $"Update the values sent for {path} to match the new type {change.NewType}.",

            ChangeType.EnumValueAdded =>
                "Review enum handling if the client assumes a fixed set of enum values.",
            ChangeType.EnumValueRemoved =>
                "Update affected client code to handle the removed enum value and verify enum handling.",

            ChangeType.InterfaceAdded =>
                "No action required for existing clients.",
            ChangeType.InterfaceRemoved =>
                $"Review fragments on interface {change.OldType} used with {change.TypeName} in affected operations.",

            ChangeType.UnionMemberAdded =>
                $"Review handling of union {change.TypeName} if the client assumes a fixed set of member types (new member: {change.NewType}).",
            ChangeType.UnionMemberRemoved =>
                $"Remove fragments on {change.OldType} from selections of union {change.TypeName} in affected operations.",

            ChangeType.DeprecationAdded =>
                $"Plan migration away from {path} before it is removed. Deprecation reason: {change.NewValue}",
            ChangeType.DeprecationRemoved =>
                "No action required for existing clients.",

            _ => throw new NotSupportedException($"Unsupported schema change type: {change.ChangeType}"),
        };
    }

    /// <summary>True when the types differ only in non-null markers, e.g. <c>String</c> → <c>String!</c>.</summary>
    private static bool IsNullabilityOnlyChange(TypeReference oldType, TypeReference newType) =>
        StripNonNull(oldType) == StripNonNull(newType);

    private static TypeReference StripNonNull(TypeReference type) => type switch
    {
        NonNullTypeReference nonNull => StripNonNull(nonNull.InnerType),
        ListTypeReference list => new ListTypeReference(StripNonNull(list.ElementType)),
        _ => type,
    };

    /// <summary>
    /// For a removed field, the fields added to the same type whose name contains, or is contained in,
    /// the removed name (case-insensitive), e.g. <c>videoUrl</c> → <c>video</c>.
    /// A naming heuristic only: it never asserts that the fields are the same concept.
    /// </summary>
    private static IReadOnlyList<string> FindPossibleMigrations(SchemaChange removed, IReadOnlyList<ClassifiedSchemaChange> allChanges)
    {
        const int MinimumNameLength = 3;
        if (removed.ChangeType != ChangeType.FieldRemoved)
        {
            return [];
        }

        var removedName = removed.FieldName!;
        return allChanges
            .Select(c => c.Change)
            .Where(c => c.ChangeType == ChangeType.FieldAdded && c.TypeName == removed.TypeName)
            .Where(c =>
            {
                var addedName = c.FieldName!;
                var shorter = addedName.Length < removedName.Length ? addedName : removedName;
                var longer = ReferenceEquals(shorter, addedName) ? removedName : addedName;
                return shorter.Length >= MinimumNameLength && longer.Contains(shorter, StringComparison.OrdinalIgnoreCase);
            })
            .Select(c => c.Path)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // ---- Validation ----------------------------------------------------------------------------

    private static void Validate(ClassifiedSchemaChange classified)
    {
        var change = classified.Change;
        void Require(bool present, string what)
        {
            if (!present)
            {
                throw new InvalidOperationException($"{change.ChangeType} change on '{change.TypeName}' is missing its {what}.");
            }
        }

        Require(!string.IsNullOrWhiteSpace(change.TypeName), "type name");
        switch (change.ChangeType)
        {
            case ChangeType.FieldAdded or ChangeType.FieldRemoved or ChangeType.FieldTypeChanged
                or ChangeType.InputFieldAdded or ChangeType.InputFieldRemoved or ChangeType.InputFieldTypeChanged
                or ChangeType.EnumValueAdded or ChangeType.EnumValueRemoved:
                Require(change.FieldName is not null, "field name");
                break;
            case ChangeType.ArgumentAdded or ChangeType.ArgumentRemoved or ChangeType.ArgumentTypeChanged:
                Require(change.FieldName is not null, "field name");
                Require(change.ArgumentName is not null, "argument name");
                break;
        }

        if (change.ChangeType is ChangeType.FieldTypeChanged or ChangeType.ArgumentTypeChanged or ChangeType.InputFieldTypeChanged)
        {
            Require(change.OldType is not null && change.NewType is not null, "old and new type");
        }
    }
}
