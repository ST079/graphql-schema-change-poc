using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Changes;

/// <summary>
/// One structural difference between an old and a new schema.
/// </summary>
/// <param name="ChangeType">What kind of difference this is.</param>
/// <param name="TypeName">The named type the change belongs to, e.g. <c>Campaign</c>.</param>
/// <param name="FieldName">
/// The member within the type, when the change is below type level: a field, an input field, or an enum value.
/// </param>
/// <param name="ArgumentName">The argument of <paramref name="FieldName"/>, for argument-level changes.</param>
/// <param name="OldType">
/// Type before the change. For interface and union membership changes, this is the interface or member type.
/// </param>
/// <param name="NewType">Type after the change. Same conventions as <paramref name="OldType"/>.</param>
/// <param name="OldValue">
/// Non-type detail before the change: the type kind, a deprecation reason,
/// or the default value of an added/removed argument or input field.
/// </param>
/// <param name="NewValue">Non-type detail after the change.</param>
/// <param name="Description">Neutral, human-readable explanation of the change.</param>
public sealed record SchemaChange(
    ChangeType ChangeType,
    string TypeName,
    string? FieldName,
    string? ArgumentName,
    TypeReference? OldType,
    TypeReference? NewType,
    string? OldValue,
    string? NewValue,
    string Description)
{
    /// <summary>
    /// Schema coordinate of the changed element:
    /// <c>Campaign</c>, <c>Campaign.video</c>, <c>CampaignStatus.ARCHIVED</c>, or <c>Query.campaign(id:)</c>.
    /// </summary>
    public string Path => (FieldName, ArgumentName) switch
    {
        (null, _) => TypeName,
        (_, null) => $"{TypeName}.{FieldName}",
        _ => $"{TypeName}.{FieldName}({ArgumentName}:)",
    };
}
