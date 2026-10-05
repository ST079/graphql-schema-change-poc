using Veel.GraphQL.SchemaIntelligence.Changes;

namespace Veel.GraphQL.SchemaIntelligence.Impact;

/// <summary>One schema change together with its impact on every client that uses the changed element.</summary>
/// <param name="Change">The classified change. Its severity stands even when no client is affected.</param>
/// <param name="ClientImpacts">One entry per affected client, ordered by client name; empty when none is affected.</param>
/// <param name="RecommendedAction">What client developers should do about this change.</param>
/// <param name="PossibleMigrations">
/// Coordinates of added elements that may replace a removed one, e.g. <c>Campaign.video</c> for <c>Campaign.videoUrl</c>.
/// These are name-based suggestions only and always need developer verification.
/// </param>
public sealed record SchemaChangeImpact(
    ClassifiedSchemaChange Change,
    IReadOnlyList<ClientImpact> ClientImpacts,
    string RecommendedAction,
    IReadOnlyList<string> PossibleMigrations)
{
    public ChangeSeverity Severity => Change.Severity;
    public bool HasClientImpact => ClientImpacts.Count > 0;
}
