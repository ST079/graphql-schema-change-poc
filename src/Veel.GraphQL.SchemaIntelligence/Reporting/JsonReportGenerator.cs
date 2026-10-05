using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Impact;

namespace Veel.GraphQL.SchemaIntelligence.Reporting;

/// <summary>
/// Formats a <see cref="ChangeReport"/> as machine-readable JSON for CI, webhooks and other tooling.
/// </summary>
/// <remarks>
/// The output is an explicit contract (the private records below), not a dump of the domain model,
/// so internal refactoring does not change it. Add properties rather than renaming or removing them;
/// bump <see cref="ReportVersion"/> for incompatible changes. The same report always produces the same text.
/// </remarks>
public sealed class JsonReportGenerator
{
    /// <summary>Version of the JSON contract.</summary>
    public const int ReportVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        // Keeps characters such as → readable; the output is a file, not HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Generate(ChangeReport report) =>
        JsonSerializer.Serialize(ToContract(report), Options) + "\n";

    private static ReportJson ToContract(ChangeReport report) => new(
        ReportVersion,
        report.ServiceName,
        report.GeneratedAt.ToUniversalTime(),
        report.OldSchemaSource,
        report.NewSchemaSource,
        new SummaryJson(
            report.TotalChanges,
            report.BreakingChanges,
            report.WarningChanges,
            report.InfoChanges,
            report.AffectedClients.Count,
            report.AffectedOperations,
            report.Analysis.Summary.Clients
                .Select(c => new ClientSummaryJson(c.ClientName, c.AffectedOperations, c.HighestSeverity))
                .ToList()),
        report.Changes.Select(ToContract).ToList(),
        report.ClientImpacts.Select(ToContract).ToList(),
        report.ScannedClients,
        report.UnaffectedOperations
            .Select(o => new OperationJson(o.ClientName, o.OperationName, o.FilePath))
            .ToList());

    private static ChangeJson ToContract(SchemaChangeImpact impact)
    {
        var change = impact.Change.Change;
        return new ChangeJson(
            change.ChangeType,
            impact.Severity,
            change.Path,
            change.TypeName,
            change.FieldName,
            change.ArgumentName,
            change.OldType?.ToString(),
            change.NewType?.ToString(),
            change.OldValue,
            change.NewValue,
            change.Description,
            impact.Change.Reason,
            impact.RecommendedAction,
            impact.HasClientImpact,
            impact.PossibleMigrations
                .Select(target => new PossibleMigrationJson(change.Path, target, "Possible", DeveloperVerificationRequired: true))
                .ToList());
    }

    private static ClientImpactJson ToContract(ClientImpact impact) => new(
        impact.ClientName,
        impact.Severity,
        impact.ChangeType,
        impact.Change.Change.Path,
        impact.SchemaType,
        impact.FieldName,
        impact.Change.Change.ArgumentName,
        impact.OldType?.ToString(),
        impact.NewType?.ToString(),
        impact.AffectedOperationNames,
        impact.AffectedOperations.Select(ToContract).ToList(),
        impact.Reason,
        impact.RecommendedAction);

    private static AffectedOperationJson ToContract(ClientOperationReference operation) => new(
        operation.OperationName,
        operation.OperationType,
        operation.FilePath,
        operation.Usages);

    // ---- JSON contract ----------------------------------------------------------------------------

    private sealed record ReportJson(
        int ReportVersion,
        string ServiceName,
        DateTimeOffset GeneratedAt,
        string? OldSchema,
        string? NewSchema,
        SummaryJson Summary,
        IReadOnlyList<ChangeJson> Changes,
        IReadOnlyList<ClientImpactJson> ClientImpacts,
        IReadOnlyList<string> ScannedClients,
        IReadOnlyList<OperationJson> UnaffectedOperations);

    private sealed record SummaryJson(
        int TotalChanges,
        int BreakingChanges,
        int WarningChanges,
        int InfoChanges,
        int AffectedClients,
        int AffectedOperations,
        IReadOnlyList<ClientSummaryJson> Clients);

    private sealed record ClientSummaryJson(string ClientName, int AffectedOperations, ChangeSeverity HighestSeverity);

    private sealed record ChangeJson(
        ChangeType ChangeType,
        ChangeSeverity Severity,
        string Path,
        string SchemaType,
        string? FieldName,
        string? ArgumentName,
        string? OldType,
        string? NewType,
        string? OldValue,
        string? NewValue,
        string Description,
        string Reason,
        string RecommendedAction,
        bool HasClientImpact,
        IReadOnlyList<PossibleMigrationJson> PossibleMigrations);

    private sealed record PossibleMigrationJson(string From, string To, string Confidence, bool DeveloperVerificationRequired);

    private sealed record ClientImpactJson(
        string ClientName,
        ChangeSeverity Severity,
        ChangeType ChangeType,
        string Path,
        string SchemaType,
        string? FieldName,
        string? ArgumentName,
        string? OldType,
        string? NewType,
        IReadOnlyList<string> AffectedOperations,
        IReadOnlyList<AffectedOperationJson> Operations,
        string Reason,
        string RecommendedAction);

    private sealed record AffectedOperationJson(
        string Name,
        GraphQLOperationType OperationType,
        string FilePath,
        IReadOnlyList<string> Usages);

    private sealed record OperationJson(string ClientName, string OperationName, string FilePath);
}
