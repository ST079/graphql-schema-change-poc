using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Impact;

/// <summary>The impact of one schema change on one client.</summary>
/// <param name="ClientName">E.g. <c>Android</c>.</param>
/// <param name="Change">The classified change; severity and change details come from here.</param>
/// <param name="AffectedOperations">The client's operations that use the changed element, ordered by name.</param>
/// <param name="Reason">Why this client is affected.</param>
/// <param name="RecommendedAction">What the client developers should do.</param>
public sealed record ClientImpact(
    string ClientName,
    ClassifiedSchemaChange Change,
    IReadOnlyList<ClientOperationReference> AffectedOperations,
    string Reason,
    string RecommendedAction)
{
    public ChangeSeverity Severity => Change.Severity;
    public ChangeType ChangeType => Change.Change.ChangeType;
    public string SchemaType => Change.Change.TypeName;
    public string? FieldName => Change.Change.FieldName;
    public TypeReference? OldType => Change.Change.OldType;
    public TypeReference? NewType => Change.Change.NewType;

    public IReadOnlyList<string> AffectedOperationNames => AffectedOperations.Select(o => o.OperationName).ToList();
}
