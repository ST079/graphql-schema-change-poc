using Veel.GraphQL.SchemaIntelligence.Changes;

namespace Veel.GraphQL.SchemaIntelligence.Impact;

/// <summary>The complete result of impact analysis: every change with its client impacts, plus a summary.</summary>
/// <param name="Changes">Ordered by severity (Breaking first), then by schema location.</param>
/// <param name="Summary">Counts derived from <paramref name="Changes"/>.</param>
public sealed record ImpactAnalysis(
    IReadOnlyList<SchemaChangeImpact> Changes,
    ImpactSummary Summary)
{
    public IReadOnlyList<ClientImpact> ClientImpacts => Changes.SelectMany(c => c.ClientImpacts).ToList();
}

/// <param name="TotalChanges">Number of schema changes, whether or not a client is affected.</param>
/// <param name="Clients">Every affected client, ordered by name.</param>
public sealed record ImpactSummary(
    int TotalChanges,
    int BreakingChanges,
    int WarningChanges,
    int InfoChanges,
    IReadOnlyList<ClientImpactSummary> Clients)
{
    public IReadOnlyList<string> AffectedClients => Clients.Select(c => c.ClientName).ToList();
}

/// <param name="AffectedOperations">Distinct operations of this client affected by at least one change.</param>
/// <param name="HighestSeverity">The most severe change affecting this client.</param>
public sealed record ClientImpactSummary(
    string ClientName,
    int AffectedOperations,
    ChangeSeverity HighestSeverity);
