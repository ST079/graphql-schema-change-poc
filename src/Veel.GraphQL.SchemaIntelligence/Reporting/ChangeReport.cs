using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Impact;

namespace Veel.GraphQL.SchemaIntelligence.Reporting;

/// <summary>
/// Everything a report needs, independent of output format. Report generators only format this;
/// they never compare schemas, inspect operations or compute impact.
/// </summary>
/// <param name="ServiceName">The service whose schema changed, e.g. <c>Cerberus</c>.</param>
/// <param name="GeneratedAt">Supplied by the caller so that generating a report is deterministic.</param>
/// <param name="Analysis">The impact analysis the report is based on.</param>
/// <param name="ScannedClients">Clients whose operations were analyzed, ordered by name; empty when none were.</param>
/// <param name="UnaffectedOperations">Scanned operations not affected by any change.</param>
/// <param name="OldSchemaSource">Where the old schema came from, e.g. a file path.</param>
/// <param name="NewSchemaSource">Where the new schema came from.</param>
public sealed record ChangeReport(
    string ServiceName,
    DateTimeOffset GeneratedAt,
    ImpactAnalysis Analysis,
    IReadOnlyList<string> ScannedClients,
    IReadOnlyList<OperationSummary> UnaffectedOperations,
    string? OldSchemaSource = null,
    string? NewSchemaSource = null)
{
    public IReadOnlyList<SchemaChangeImpact> Changes => Analysis.Changes;
    public IReadOnlyList<ClientImpact> ClientImpacts => Analysis.ClientImpacts;

    public int TotalChanges => Analysis.Summary.TotalChanges;
    public int BreakingChanges => Analysis.Summary.BreakingChanges;
    public int WarningChanges => Analysis.Summary.WarningChanges;
    public int InfoChanges => Analysis.Summary.InfoChanges;

    public IReadOnlyList<string> AffectedClients => Analysis.Summary.AffectedClients;

    /// <summary>Distinct affected operations across all clients.</summary>
    public int AffectedOperations => Analysis.Summary.Clients.Sum(c => c.AffectedOperations);

    /// <summary>
    /// Builds a report, deriving the scanned clients and the unaffected operations
    /// from the operations that were analyzed.
    /// </summary>
    /// <param name="clients">Every client that was scanned, including clients without operations.</param>
    /// <param name="operations">Every operation that was analyzed.</param>
    public static ChangeReport Create(
        string serviceName,
        DateTimeOffset generatedAt,
        ImpactAnalysis analysis,
        IEnumerable<ClientDefinition> clients,
        IEnumerable<GraphQLOperation> operations,
        string? oldSchemaSource = null,
        string? newSchemaSource = null)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("A service name is required.", nameof(serviceName));
        }

        var affected = analysis.ClientImpacts
            .SelectMany(i => i.AffectedOperations)
            .Select(o => new OperationSummary(o.ClientName, o.OperationName, o.FilePath))
            .ToHashSet();

        var unaffected = operations
            .Select(o => new OperationSummary(o.ClientName, o.Name, o.FilePath))
            .Where(o => !affected.Contains(o))
            .Distinct()
            .OrderBy(o => o.ClientName, StringComparer.Ordinal)
            .ThenBy(o => o.OperationName, StringComparer.Ordinal)
            .ThenBy(o => o.FilePath, StringComparer.Ordinal)
            .ToList();

        var scannedClients = clients.Select(c => c.Name).Distinct().Order(StringComparer.Ordinal).ToList();

        return new ChangeReport(serviceName, generatedAt, analysis, scannedClients, unaffected, oldSchemaSource, newSchemaSource);
    }
}

/// <summary>Identifies one client operation in a report.</summary>
public sealed record OperationSummary(string ClientName, string OperationName, string FilePath);
