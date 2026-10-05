using Veel.GraphQL.SchemaIntelligence.Changes;

namespace Veel.GraphQL.SchemaIntelligence.Clients;

/// <summary>A client operation that uses a schema element touched by a change.</summary>
/// <param name="ClientName">E.g. <c>Android</c>.</param>
/// <param name="OperationName">E.g. <c>GetCampaign</c>.</param>
/// <param name="OperationType">Query, mutation or subscription.</param>
/// <param name="FilePath">Document containing the operation.</param>
/// <param name="SchemaType">Type of the changed element, e.g. <c>Campaign</c>.</param>
/// <param name="FieldName">Field, input field or enum value of the changed element, when below type level.</param>
/// <param name="Usages">
/// Where the operation uses the element, e.g. <c>campaign.videoUrl</c>
/// or <c>campaign.videoUrl (via fragment CampaignFields)</c>.
/// </param>
/// <param name="Change">The classified change this reference was found for.</param>
public sealed record ClientOperationReference(
    string ClientName,
    string OperationName,
    GraphQLOperationType OperationType,
    string FilePath,
    string SchemaType,
    string? FieldName,
    IReadOnlyList<string> Usages,
    ClassifiedSchemaChange Change);
