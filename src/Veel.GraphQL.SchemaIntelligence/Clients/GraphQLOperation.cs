using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Clients;

public enum GraphQLOperationType
{
    Query,
    Mutation,
    Subscription,
}

/// <summary>
/// A client operation as written in its document, independent of the parsing library.
/// Selections are kept as a tree; fragment spreads are resolved against <see cref="Fragments"/>
/// when field references are computed.
/// </summary>
/// <param name="ClientName">The client the operation belongs to.</param>
/// <param name="Name">Operation name; for an anonymous operation, the file name without extension.</param>
/// <param name="OperationType">Query, mutation or subscription.</param>
/// <param name="FilePath">The document the operation was read from.</param>
/// <param name="Selections">Top-level selection set.</param>
/// <param name="Variables">Declared variables, e.g. <c>$id: ID!</c>.</param>
/// <param name="Fragments">Every fragment this operation uses, directly or through other fragments, by name.</param>
public sealed record GraphQLOperation(
    string ClientName,
    string Name,
    GraphQLOperationType OperationType,
    string FilePath,
    IReadOnlyList<Selection> Selections,
    IReadOnlyList<VariableDefinition> Variables,
    IReadOnlyDictionary<string, FragmentDefinition> Fragments);

public sealed record VariableDefinition(string Name, TypeReference Type);

/// <summary>A named fragment, possibly defined in another file of the same client.</summary>
public sealed record FragmentDefinition(
    string Name,
    string TypeCondition,
    string FilePath,
    IReadOnlyList<Selection> Selections);

public abstract record Selection;

/// <param name="Name">The schema field name.</param>
/// <param name="Alias">The response key, when aliased (<c>alias: name</c>).</param>
/// <param name="Arguments">Names of the arguments passed to the field.</param>
/// <param name="Selections">Sub-selections; empty for leaf fields.</param>
public sealed record FieldSelection(
    string Name,
    string? Alias,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<Selection> Selections) : Selection;

/// <summary><c>... on Type { }</c>, or <c>... { }</c> when <paramref name="TypeCondition"/> is null.</summary>
public sealed record InlineFragmentSelection(
    string? TypeCondition,
    IReadOnlyList<Selection> Selections) : Selection;

/// <summary><c>...FragmentName</c></summary>
public sealed record FragmentSpreadSelection(string FragmentName) : Selection;

/// <summary>
/// One field selected by an operation, resolved against a schema to the field's schema coordinate.
/// </summary>
/// <param name="TypeName">The parent type the field is selected on, e.g. <c>Campaign</c>.</param>
/// <param name="FieldName">The schema field name, e.g. <c>videoUrl</c> (never the alias).</param>
/// <param name="Path">Field names from the operation root, e.g. <c>campaign.video.url</c>.</param>
/// <param name="Alias">The alias used in the operation, if any.</param>
/// <param name="ReturnType">Named type the field returns; null when the field does not exist in the schema.</param>
/// <param name="Arguments">Arguments passed, mapped to their named input type (null when unknown to the schema).</param>
/// <param name="Fragment">The innermost named fragment the field was selected through, if any.</param>
public sealed record GraphQLFieldReference(
    string TypeName,
    string FieldName,
    string Path,
    string? Alias,
    string? ReturnType,
    IReadOnlyDictionary<string, string?> Arguments,
    string? Fragment)
{
    /// <summary>Schema coordinate, e.g. <c>Campaign.videoUrl</c>.</summary>
    public string Coordinate => $"{TypeName}.{FieldName}";
}
