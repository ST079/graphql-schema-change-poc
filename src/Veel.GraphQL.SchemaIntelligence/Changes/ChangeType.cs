namespace Veel.GraphQL.SchemaIntelligence.Changes;

/// <summary>
/// The kind of structural difference between two schemas.
/// Says nothing about severity; that is decided by change classification.
/// New members may be added over time, so consumers should not assume the set is closed.
/// </summary>
public enum ChangeType
{
    // Named types
    TypeAdded,
    TypeRemoved,
    TypeKindChanged,

    // Fields of object and interface types
    FieldAdded,
    FieldRemoved,
    FieldTypeChanged,

    // Arguments of object and interface fields
    ArgumentAdded,
    ArgumentRemoved,
    ArgumentTypeChanged,

    // Fields of input object types
    InputFieldAdded,
    InputFieldRemoved,
    InputFieldTypeChanged,

    // Enum values
    EnumValueAdded,
    EnumValueRemoved,

    // Interfaces implemented by object and interface types
    InterfaceAdded,
    InterfaceRemoved,

    // Member types of unions
    UnionMemberAdded,
    UnionMemberRemoved,

    // @deprecated on fields, arguments, input fields and enum values
    DeprecationAdded,
    DeprecationRemoved,
}
