using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class SchemaLoaderTests
{
    // ---- Valid schemas -------------------------------------------------------------------------

    [Fact]
    public void LoadFromFile_DemoOldSchema_ProducesCampaignWithVideoUrl()
    {
        var schema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/old-schema.graphql"));

        Assert.Equal(["Campaign", "Query", "User"], schema.Types.Select(t => t.Name));
        Assert.Equal("Query", schema.QueryTypeName);
        Assert.Null(schema.MutationTypeName);

        var campaign = schema.FindType("Campaign")!;
        Assert.Equal(SchemaTypeKind.Object, campaign.Kind);
        Assert.Equal(
            ["id: ID!", "title: String!", "videoUrl: String"],
            campaign.Fields.Select(f => $"{f.Name}: {f.Type}"));
    }

    [Fact]
    public void LoadFromFile_DemoNewSchema_ProducesCampaignVideoType()
    {
        var schema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/new-schema.graphql"));

        Assert.Equal(["Campaign", "CampaignVideo", "Query", "User"], schema.Types.Select(t => t.Name));
        Assert.Null(schema.FindType("Campaign")!.FindField("videoUrl"));
        Assert.Equal(new NamedTypeReference("CampaignVideo"), schema.FindType("Campaign")!.FindField("video")!.Type);
        Assert.Equal(
            ["id: ID!", "url: String!"],
            schema.FindType("CampaignVideo")!.Fields.Select(f => $"{f.Name}: {f.Type}"));
    }

    [Fact]
    public void Parse_AllTypeKinds_AreMapped()
    {
        var schema = SchemaLoader.Parse("""
            scalar DateTime

            interface Node { id: ID! }

            enum CampaignStatus { DRAFT ACTIVE ARCHIVED }

            input CampaignFilter {
              status: CampaignStatus = ACTIVE
              tags: [String!]
            }

            type Campaign implements Node {
              id: ID!
              createdAt: DateTime!
              status: CampaignStatus!
            }

            type Banner { id: ID! }

            union SearchResult = Campaign | Banner

            type Query {
              campaigns(filter: CampaignFilter, first: Int = 10): [Campaign!]!
              search(term: String!): [SearchResult]
            }
            """);

        Assert.Equal(SchemaTypeKind.Scalar, schema.FindType("DateTime")!.Kind);
        Assert.Equal(SchemaTypeKind.Interface, schema.FindType("Node")!.Kind);
        Assert.Equal(["Node"], schema.FindType("Campaign")!.Interfaces);
        Assert.Equal(["DRAFT", "ACTIVE", "ARCHIVED"], schema.FindType("CampaignStatus")!.EnumValues.Select(v => v.Name));
        Assert.Equal(["Campaign", "Banner"], schema.FindType("SearchResult")!.PossibleTypes);

        var filter = schema.FindType("CampaignFilter")!;
        Assert.Equal(SchemaTypeKind.InputObject, filter.Kind);
        Assert.Equal("ACTIVE", filter.FindInputField("status")!.DefaultValue);
        Assert.Equal("[String!]", filter.FindInputField("tags")!.Type.ToString());

        var campaigns = schema.FindType("Query")!.FindField("campaigns")!;
        Assert.Equal("[Campaign!]!", campaigns.Type.ToString());
        Assert.Equal("CampaignFilter", campaigns.FindArgument("filter")!.Type.ToString());
        Assert.Equal("10", campaigns.FindArgument("first")!.DefaultValue);
    }

    [Fact]
    public void Parse_DeprecatedDirective_IsCapturedWithReason()
    {
        var schema = SchemaLoader.Parse("""
            enum Status { OLD @deprecated NEW }

            type Campaign {
              videoUrl: String @deprecated(reason: "Use video")
              video: String
            }

            type Query { campaign: Campaign, status: Status }
            """);

        var campaign = schema.FindType("Campaign")!;
        Assert.True(campaign.FindField("videoUrl")!.IsDeprecated);
        Assert.Equal("Use video", campaign.FindField("videoUrl")!.DeprecationReason);
        Assert.False(campaign.FindField("video")!.IsDeprecated);

        var oldValue = schema.FindType("Status")!.FindEnumValue("OLD")!;
        Assert.True(oldValue.IsDeprecated);
        Assert.Equal("No longer supported", oldValue.DeprecationReason);
    }

    [Fact]
    public void Parse_ExplicitSchemaDefinition_UsesDeclaredRootTypes()
    {
        var schema = SchemaLoader.Parse("""
            schema { query: RootQuery mutation: RootMutation }
            type RootQuery { ping: String }
            type RootMutation { publish(id: ID!): Boolean! }
            """);

        Assert.Equal("RootQuery", schema.QueryTypeName);
        Assert.Equal("RootMutation", schema.MutationTypeName);
        Assert.Null(schema.SubscriptionTypeName);
    }

    [Fact]
    public void Parse_ConventionalRootTypes_AreDetected()
    {
        var schema = SchemaLoader.Parse("""
            type Query { ping: String }
            type Mutation { ping: String }
            type Subscription { ping: String }
            """);

        Assert.Equal("Mutation", schema.MutationTypeName);
        Assert.Equal("Subscription", schema.SubscriptionTypeName);
    }

    [Fact]
    public void Parse_DirectiveDefinitionsAndDescriptions_AreAccepted()
    {
        var schema = SchemaLoader.Parse("""
            directive @cost(weight: Int!) on FIELD_DEFINITION

            "A marketing campaign"
            type Campaign {
              "Campaign title"
              title: String @cost(weight: 1)
            }

            type Query { campaign: Campaign }
            """);

        Assert.Equal(["Campaign", "Query"], schema.Types.Select(t => t.Name));
    }

    [Fact]
    public void TypeReference_SupportsStructuralComparisonAndNullabilityHelpers()
    {
        var nonNullString = new NonNullTypeReference(new NamedTypeReference("String"));
        var list = new ListTypeReference(nonNullString);

        Assert.Equal(new NonNullTypeReference(new NamedTypeReference("String")), nonNullString);
        Assert.NotEqual<TypeReference>(nonNullString, new NamedTypeReference("String"));
        Assert.True(nonNullString.IsNonNull);
        Assert.Equal(new NamedTypeReference("String"), nonNullString.Nullable);
        Assert.Equal("String", list.NamedType);
        Assert.Equal("[String!]", list.ToString());
    }

    // ---- Invalid schemas -----------------------------------------------------------------------

    [Fact]
    public void LoadFromFile_MissingFile_Throws()
    {
        var ex = Assert.Throws<SchemaLoadException>(() => SchemaLoader.LoadFromFile("does-not-exist.graphql"));

        Assert.Contains("does not exist", ex.Message);
        Assert.Equal("does-not-exist.graphql", ex.SchemaSource);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Parse_EmptySchema_Throws(string sdl)
    {
        var ex = Assert.Throws<SchemaLoadException>(() => SchemaLoader.Parse(sdl));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Parse_SyntaxError_ReportsLineAndColumn()
    {
        var ex = Assert.Throws<SchemaLoadException>(() => SchemaLoader.Parse("type Query {\n  campaign: \n}", "broken.graphql"));

        Assert.Contains("invalid GraphQL", ex.Message);
        Assert.Contains("broken.graphql", ex.Message);
        Assert.Equal(3, ex.Line);
        Assert.NotNull(ex.Column);
    }

    [Theory]
    [InlineData("type Campaign { id: ID! }", "query root type 'Query' is not defined")]
    [InlineData("type Query { campaign: Campaign }", "Field 'Query.campaign' references unknown type 'Campaign'")]
    [InlineData("type Query { a: String } type Query { b: String }", "Type 'Query' is defined more than once")]
    [InlineData("type Query { a: String a: Int }", "Duplicate field of type 'Query': 'a'")]
    [InlineData("type Query { a(x: Int, x: Int): String }", "Duplicate argument of field 'Query.a': 'x'")]
    [InlineData("enum E { A A } type Query { e: E }", "Duplicate value of enum 'E': 'A'")]
    [InlineData("input F { a: Int } type Query { f: F }", "Field 'Query.f' must use an output type, but 'F' is InputObject")]
    [InlineData("type C { id: ID } type Query { c(arg: C): C }", "Argument 'Query.c(arg)' must use an input type, but 'C' is Object")]
    [InlineData("input F { c: C } type C { id: ID } type Query { c(f: F): C }", "Input field 'F.c' must use an input type, but 'C' is Object")]
    [InlineData("type A { id: ID } type B implements A { id: ID } type Query { b: B }", "Type 'B' implements 'A', which is Object, not Interface")]
    [InlineData("scalar S union U = S type Query { u: U }", "Union 'U' includes 'S', which is Scalar, not Object")]
    [InlineData("enum Query { A }", "The query root type 'Query' must be an object type")]
    [InlineData("interface N { id: ID } type A implements N & N { id: ID } type Query { a: A }", "Duplicate interface implemented by 'A': 'N'")]
    [InlineData("type A { id: ID } union U = A | A type Query { u: U }", "Duplicate member type of union 'U': 'A'")]
    [InlineData("schema { mutation: Mutation } type Mutation { a: String }", "does not declare a query root type")]
    [InlineData("type Query { a: String } query GetA { a }", "Schema contains an operation or fragment")]
    [InlineData("type Query { a: String } extend type Query { b: String }", "is not supported yet")]
    public void Parse_InvalidSchema_ReportsValidationError(string sdl, string expectedError)
    {
        var ex = Assert.Throws<SchemaLoadException>(() => SchemaLoader.Parse(sdl));

        Assert.Contains(ex.Errors, e => e.Contains(expectedError));
        Assert.StartsWith("Schema failed validation", ex.Message);
    }

    [Fact]
    public void Parse_MultipleProblems_AreAllReported()
    {
        var ex = Assert.Throws<SchemaLoadException>(() => SchemaLoader.Parse("""
            type Query { a: Missing1, b: Missing2 }
            """));

        Assert.Equal(2, ex.Errors.Count);
    }
}
