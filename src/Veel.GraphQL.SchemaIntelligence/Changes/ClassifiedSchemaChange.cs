namespace Veel.GraphQL.SchemaIntelligence.Changes;

/// <summary>A detected schema change together with its severity and the reason for it.</summary>
/// <param name="Change">The original change, unmodified.</param>
/// <param name="Severity">How significant the change is for the schema contract.</param>
/// <param name="Reason">Why the change was given this severity.</param>
public sealed record ClassifiedSchemaChange(
    SchemaChange Change,
    ChangeSeverity Severity,
    string Reason);
