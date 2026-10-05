using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class SchemaDifferTests
{
    private const string QueryType = "type Query { campaign: Campaign }";

    private static IReadOnlyList<SchemaChange> Diff(string oldSdl, string newSdl) =>
        new SchemaDiffer().Compare(SchemaLoader.Parse(oldSdl), SchemaLoader.Parse(newSdl));

    /// <summary>Compact "ChangeType Path old→new" rendering for readable assertions.</summary>
    private static string Format(SchemaChange c) =>
        $"{c.ChangeType} {c.Path}" + (c.OldType is null && c.NewType is null ? "" :$" {c.OldType}→{c.NewType}");

    [Fact]
    public void Compare_IdenticalSchemas_ReturnsNoChanges()
    {
        const string sdl = $"type Campaign {{ id: ID! title: String! }} {QueryType}";

        Assert.Empty(Diff(sdl, sdl));
    }

    [Fact]
    public void Compare_TypeAdded_IsDetected()
    {
        var changes = Diff(
            $"type Campaign {{ id: ID! }} {QueryType}",
            $"type Campaign {{ id: ID! }} type CampaignVideo {{ id: ID! url: String! }} {QueryType}");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.TypeAdded, change.ChangeType);
        Assert.Equal("CampaignVideo", change.TypeName);
        Assert.Null(change.FieldName);
        Assert.Equal("Object", change.NewValue);
    }

    [Fact]
    public void Compare_TypeRemoved_IsDetectedWithoutPerFieldNoise()
    {
        var changes = Diff(
            $"type Campaign {{ id: ID! }} type CampaignVideo {{ id: ID! url: String! }} {QueryType}",
            $"type Campaign {{ id: ID! }} {QueryType}");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.TypeRemoved, change.ChangeType);
        Assert.Equal("CampaignVideo", change.TypeName);
        Assert.Equal("Object", change.OldValue);
    }

    [Fact]
    public void Compare_FieldAdded_IsDetected()
    {
        var changes = Diff(
            $"type Campaign {{ id: ID! }} {QueryType}",
            $"type Campaign {{ id: ID! title: String! }} {QueryType}");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.FieldAdded, change.ChangeType);
        Assert.Equal("Campaign", change.TypeName);
        Assert.Equal("title", change.FieldName);
        Assert.Equal("Campaign.title", change.Path);
        Assert.Null(change.OldType);
        Assert.Equal("String!", change.NewType!.ToString());
    }

    [Fact]
    public void Compare_FieldRemoved_IsDetected()
    {
        var changes = Diff(
            $"type Campaign {{ id: ID! title: String! }} {QueryType}",
            $"type Campaign {{ id: ID! }} {QueryType}");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.FieldRemoved, change.ChangeType);
        Assert.Equal("Campaign.title", change.Path);
        Assert.Equal("String!", change.OldType!.ToString());
        Assert.Null(change.NewType);
    }

    [Theory]
    [InlineData("String", "String!")]
    [InlineData("String!", "String")]
    [InlineData("String", "Int")]
    [InlineData("String", "CampaignTitle")]
    [InlineData("[Campaign]", "[Campaign!]!")]
    [InlineData("[String]", "String")]
    public void Compare_FieldTypeChanged_IsDetectedWithOldAndNewType(string oldType, string newType)
    {
        const string extraTypes = "scalar CampaignTitle";
        var changes = Diff(
            $"type Campaign {{ id: ID! title: {oldType} }} {extraTypes} {QueryType}",
            $"type Campaign {{ id: ID! title: {newType} }} {extraTypes} {QueryType}");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.FieldTypeChanged, change.ChangeType);
        Assert.Equal("Campaign.title", change.Path);
        Assert.Equal(oldType, change.OldType!.ToString());
        Assert.Equal(newType, change.NewType!.ToString());
    }

    public static TheoryData<string, TypeReference> TypeShapes => new()
    {
        { "String", new NamedTypeReference("String") },
        { "String!", new NonNullTypeReference(new NamedTypeReference("String")) },
        { "[String]", new ListTypeReference(new NamedTypeReference("String")) },
        { "[String!]", new ListTypeReference(new NonNullTypeReference(new NamedTypeReference("String"))) },
        { "[String]!", new NonNullTypeReference(new ListTypeReference(new NamedTypeReference("String"))) },
        { "[String!]!", new NonNullTypeReference(new ListTypeReference(new NonNullTypeReference(new NamedTypeReference("String")))) },
    };

    [Theory]
    [MemberData(nameof(TypeShapes))]
    public void Compare_NullableAndListWrappers_AreRepresentedStructurally(string sdlType, TypeReference expected)
    {
        // Every shape differs from plain Int, so each produces one FieldTypeChanged carrying the exact new type.
        var change = Assert.Single(Diff("type Query { f: Int }", $"type Query {{ f: {sdlType} }}"));

        Assert.Equal(ChangeType.FieldTypeChanged, change.ChangeType);
        Assert.Equal(new NamedTypeReference("Int"), change.OldType);
        Assert.Equal(expected, change.NewType);
        Assert.Equal(sdlType, change.NewType!.ToString());
        Assert.Equal("String", change.NewType.NamedType);
    }

    [Theory]
    [InlineData("[String]", "[String!]")]   // inner nullability only
    [InlineData("[String]", "[String]!")]   // outer nullability only
    [InlineData("[String!]!", "[String]")]  // relaxed
    [InlineData("[[String]]", "[String]")]  // list depth
    public void Compare_ListWrapperChanges_AreDetected(string oldType, string newType)
    {
        var change = Assert.Single(Diff($"type Query {{ f: {oldType} }}", $"type Query {{ f: {newType} }}"));

        Assert.Equal(ChangeType.FieldTypeChanged, change.ChangeType);
        Assert.Equal(oldType, change.OldType!.ToString());
        Assert.Equal(newType, change.NewType!.ToString());
    }

    [Fact]
    public void Compare_RepeatedRuns_ProduceIdenticalResults()
    {
        var oldSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/old-schema.graphql"));
        var newSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/new-schema.graphql"));
        var differ = new SchemaDiffer();

        var first = differ.Compare(oldSchema, newSchema);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(first, differ.Compare(oldSchema, newSchema));
        }
    }

    [Fact]
    public void Compare_DemoSchemas_ReportsRemovalAndAdditionNotRename()
    {
        var changes = new SchemaDiffer().Compare(
            SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/old-schema.graphql")),
            SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/new-schema.graphql")));

        Assert.Equal(
            [
                "FieldAdded Campaign.video →CampaignVideo",
                "FieldRemoved Campaign.videoUrl String→",
                "TypeAdded CampaignVideo",
            ],
            changes.Select(Format));
    }

    [Fact]
    public void Compare_MultipleChanges_AreAllReported()
    {
        var changes = Diff(
            $"type Campaign {{ id: ID! title: String! budget: Int }} {QueryType}",
            $"type Campaign {{ id: ID! video: CampaignVideo budget: Float }} type CampaignVideo {{ id: ID! }} {QueryType}");

        Assert.Equal(
            [
                "FieldTypeChanged Campaign.budget Int→Float",
                "FieldRemoved Campaign.title String!→",
                "FieldAdded Campaign.video →CampaignVideo",
                "TypeAdded CampaignVideo",
            ],
            changes.Select(Format));
    }

    [Fact]
    public void Compare_EnumValueAdded_IsDetected()
    {
        var changes = Diff(
            "enum CampaignStatus { DRAFT PUBLISHED } type Query { status: CampaignStatus }",
            "enum CampaignStatus { DRAFT PUBLISHED ARCHIVED } type Query { status: CampaignStatus }");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.EnumValueAdded, change.ChangeType);
        Assert.Equal("CampaignStatus.ARCHIVED", change.Path);
    }

    [Fact]
    public void Compare_EnumValueRemoved_IsDetected()
    {
        var changes = Diff(
            "enum CampaignStatus { DRAFT PUBLISHED ARCHIVED } type Query { status: CampaignStatus }",
            "enum CampaignStatus { DRAFT PUBLISHED } type Query { status: CampaignStatus }");

        var change = Assert.Single(changes);
        Assert.Equal(ChangeType.EnumValueRemoved, change.ChangeType);
        Assert.Equal("CampaignStatus.ARCHIVED", change.Path);
    }

    [Fact]
    public void Compare_ArgumentChanges_AreDetected()
    {
        var changes = Diff(
            "type Query { campaigns(first: Int, status: String, legacy: Boolean): String }",
            "type Query { campaigns(first: Int!, status: String, after: String): String }");

        Assert.Equal(
            [
                "ArgumentAdded Query.campaigns(after:) →String",
                "ArgumentTypeChanged Query.campaigns(first:) Int→Int!",
                "ArgumentRemoved Query.campaigns(legacy:) Boolean→",
            ],
            changes.Select(Format));
        Assert.All(changes, c => Assert.Equal("campaigns", c.FieldName));
    }

    [Fact]
    public void Compare_AddedAndRemovedArguments_CarryTheirDefaultValue()
    {
        var changes = Diff(
            "type Query { campaigns(legacy: Boolean = false): String }",
            "type Query { campaigns(first: Int! = 10, after: String): String }");

        Assert.Equal("10", changes.Single(c => c.ArgumentName == "first").NewValue);
        Assert.Null(changes.Single(c => c.ArgumentName == "after").NewValue);
        Assert.Equal("false", changes.Single(c => c.ArgumentName == "legacy").OldValue);
    }

    [Fact]
    public void Compare_InputFieldChanges_AreDetected()
    {
        var changes = Diff(
            "input Filter { status: String, limit: Int } type Query { campaigns(filter: Filter): String }",
            "input Filter { status: String!, tag: String } type Query { campaigns(filter: Filter): String }");

        Assert.Equal(
            [
                "InputFieldRemoved Filter.limit Int→",
                "InputFieldTypeChanged Filter.status String→String!",
                "InputFieldAdded Filter.tag →String",
            ],
            changes.Select(Format));
    }

    [Fact]
    public void Compare_DeprecationChanges_AreDetected()
    {
        var changes = Diff(
            "enum S { A B @deprecated } type Query { videoUrl: String, s: S }",
            "enum S { A @deprecated(reason: \"Use B\") B } type Query { videoUrl: String @deprecated(reason: \"Use video\"), s: S }");

        Assert.Equal(
            ["DeprecationAdded Query.videoUrl", "DeprecationAdded S.A", "DeprecationRemoved S.B"],
            changes.Select(Format));
        Assert.Equal("Use video", changes[0].NewValue);
    }

    [Fact]
    public void Compare_InterfaceAndUnionMembershipChanges_AreDetected()
    {
        var changes = Diff(
            "interface Node { id: ID! } type A { id: ID! } type B { id: ID! } union U = A type Query { u: U, n: Node }",
            "interface Node { id: ID! } type A implements Node { id: ID! } type B { id: ID! } union U = B type Query { u: U, n: Node }");

        Assert.Equal(
            ["InterfaceAdded A →Node", "UnionMemberAdded U →B", "UnionMemberRemoved U A→"],
            changes.Select(Format));
    }

    [Fact]
    public void Compare_TypeKindChanged_IsReportedOnce()
    {
        var changes = Diff(
            "type Node { id: ID! } type Query { n: Node }",
            "interface Node { id: ID! } type Impl implements Node { id: ID! } type Query { n: Node }");

        Assert.Equal(["TypeAdded Impl", "TypeKindChanged Node"], changes.Select(Format));
        Assert.Equal("Object", changes[1].OldValue);
        Assert.Equal("Interface", changes[1].NewValue);
    }

    [Fact]
    public void Compare_SameSchemasInDifferentDeclarationOrder_ProduceIdenticalOrderedOutput()
    {
        const string oldA = "type Campaign { id: ID! a: Int b: Int } type Zeta { z: Int } type Query { c: Campaign, z: Zeta }";
        const string oldB = "type Query { z: Zeta, c: Campaign } type Zeta { z: Int } type Campaign { b: Int a: Int id: ID! }";
        const string newA = "type Campaign { id: ID! c: Int d: Int } type Alpha { a: Int } type Query { c: Campaign, a: Alpha }";
        const string newB = "type Alpha { a: Int } type Query { a: Alpha, c: Campaign } type Campaign { d: Int c: Int id: ID! }";

        var first = Diff(oldA, newA);
        var second = Diff(oldB, newB);

        Assert.Equal(first, second);
        Assert.Equal(
            [
                "TypeAdded Alpha",
                "FieldRemoved Campaign.a Int→",
                "FieldRemoved Campaign.b Int→",
                "FieldAdded Campaign.c →Int",
                "FieldAdded Campaign.d →Int",
                "FieldAdded Query.a →Alpha",
                "FieldRemoved Query.z Zeta→",
                "TypeRemoved Zeta",
            ],
            first.Select(Format));
    }

    [Fact]
    public void Compare_SwappedInputs_InvertsAdditionsAndRemovals()
    {
        var oldSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/old-schema.graphql"));
        var newSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/new-schema.graphql"));

        var changes = new SchemaDiffer().Compare(newSchema, oldSchema);

        Assert.Equal(
            [
                "FieldRemoved Campaign.video CampaignVideo→",
                "FieldAdded Campaign.videoUrl →String",
                "TypeRemoved CampaignVideo",
            ],
            changes.Select(Format));
    }
}
