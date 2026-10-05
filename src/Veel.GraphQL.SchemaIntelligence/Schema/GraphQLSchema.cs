namespace Veel.GraphQL.SchemaIntelligence.Schema;

/// <summary>
/// Parser-independent representation of a loaded GraphQL schema (SDL).
/// This is the input to schema diffing; it carries no AST types from the parsing library.
/// </summary>
public sealed class GraphQLSchema
{
    public static readonly IReadOnlySet<string> BuiltInScalars =
        new HashSet<string>(StringComparer.Ordinal) { "Int", "Float", "String", "Boolean", "ID" };

    private readonly Dictionary<string, SchemaType> _typesByName;

    public GraphQLSchema(
        IEnumerable<SchemaType> types,
        string queryTypeName,
        string? mutationTypeName = null,
        string? subscriptionTypeName = null)
    {
        // Sorted by name so anything enumerating the schema is deterministic.
        Types = types.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        _typesByName = Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        QueryTypeName = queryTypeName;
        MutationTypeName = mutationTypeName;
        SubscriptionTypeName = subscriptionTypeName;
    }

    /// <summary>User-defined types, ordered by name. Built-in scalars are implicit and not included.</summary>
    public IReadOnlyList<SchemaType> Types { get; }

    public string QueryTypeName { get; }
    public string? MutationTypeName { get; }
    public string? SubscriptionTypeName { get; }

    public SchemaType? FindType(string name) => _typesByName.GetValueOrDefault(name);
}

public enum SchemaTypeKind
{
    Scalar,
    Object,
    Interface,
    Union,
    Enum,
    InputObject,
}

/// <summary>
/// A named type. Only the members relevant to <see cref="Kind"/> are populated; the others are empty.
/// </summary>
public sealed record SchemaType(
    string Name,
    SchemaTypeKind Kind,
    IReadOnlyList<SchemaField> Fields,
    IReadOnlyList<SchemaInputValue> InputFields,
    IReadOnlyList<SchemaEnumValue> EnumValues,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> PossibleTypes)
{
    public SchemaField? FindField(string name) => Fields.FirstOrDefault(f => f.Name == name);
    public SchemaInputValue? FindInputField(string name) => InputFields.FirstOrDefault(f => f.Name == name);
    public SchemaEnumValue? FindEnumValue(string name) => EnumValues.FirstOrDefault(v => v.Name == name);
}

/// <summary>Output field of an object or interface type.</summary>
public sealed record SchemaField(
    string Name,
    TypeReference Type,
    IReadOnlyList<SchemaInputValue> Arguments,
    bool IsDeprecated = false,
    string? DeprecationReason = null)
{
    public SchemaInputValue? FindArgument(string name) => Arguments.FirstOrDefault(a => a.Name == name);
}

/// <summary>A field argument or an input object field.</summary>
public sealed record SchemaInputValue(
    string Name,
    TypeReference Type,
    string? DefaultValue = null,
    bool IsDeprecated = false,
    string? DeprecationReason = null);

public sealed record SchemaEnumValue(
    string Name,
    bool IsDeprecated = false,
    string? DeprecationReason = null);

/// <summary>
/// A type as written at a usage site, e.g. <c>String</c>, <c>[Campaign!]!</c>.
/// Records give structural equality, so two references can be compared with <c>==</c>.
/// </summary>
public abstract record TypeReference
{
    /// <summary>The innermost named type, e.g. <c>Campaign</c> for <c>[Campaign!]!</c>.</summary>
    public abstract string NamedType { get; }

    public bool IsNonNull => this is NonNullTypeReference;

    /// <summary>The same reference with an outer non-null wrapper removed, if any.</summary>
    public TypeReference Nullable => this is NonNullTypeReference nonNull ? nonNull.InnerType : this;

    public abstract override string ToString();
}

public sealed record NamedTypeReference(string Name) : TypeReference
{
    public override string NamedType => Name;
    public override string ToString() => Name;
}

public sealed record ListTypeReference(TypeReference ElementType) : TypeReference
{
    public override string NamedType => ElementType.NamedType;
    public override string ToString() => $"[{ElementType}]";
}

public sealed record NonNullTypeReference(TypeReference InnerType) : TypeReference
{
    public override string NamedType => InnerType.NamedType;
    public override string ToString() => $"{InnerType}!";
}
