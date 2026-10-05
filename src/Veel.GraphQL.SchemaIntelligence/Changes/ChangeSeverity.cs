namespace Veel.GraphQL.SchemaIntelligence.Changes;

/// <summary>
/// How significant a schema change is for the schema contract, independent of which clients use it.
/// Ordered from least to most severe, so severities can be compared and the maximum taken.
/// </summary>
public enum ChangeSeverity
{
    /// <summary>Normally backward-compatible.</summary>
    Info,

    /// <summary>Needs developer attention or migration, but does not break existing clients immediately.</summary>
    Warning,

    /// <summary>The existing contract may no longer work for clients that rely on it.</summary>
    Breaking,
}
