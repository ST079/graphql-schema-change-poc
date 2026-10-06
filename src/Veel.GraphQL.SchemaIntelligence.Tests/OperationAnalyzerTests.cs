using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class OperationAnalyzerTests
{
    private static readonly GraphQLSchema Schema = SchemaLoader.Parse("""
        type Campaign {
          id: ID!
          title: String!
          videoUrl: String
          video: CampaignVideo
          status: CampaignStatus
        }
        type CampaignVideo { id: ID! url: String! }
        enum CampaignStatus { DRAFT ACTIVE }
        type User { id: ID! name: String! }
        input CampaignInput { title: String! }
        type Query {
          campaign(id: ID): Campaign
          campaigns(status: CampaignStatus): [Campaign]
          user: User
        }
        type Mutation { publishCampaign(input: CampaignInput!): Campaign }
        type Subscription { campaignUpdated: Campaign }
        """);

    private readonly OperationAnalyzer _analyzer = new();

    private static ClassifiedSchemaChange Classified(
        ChangeType changeType, string typeName, string? field = null, string? argument = null) =>
        new ChangeClassifier().Classify(
            new SchemaChange(changeType, typeName, field, argument, null, null, null, null, $"{changeType} {typeName}.{field}"));

    private static readonly ClassifiedSchemaChange VideoUrlRemoved = Classified(ChangeType.FieldRemoved, "Campaign", "videoUrl");

    private IReadOnlyList<GraphQLOperation> Parse(string document, string client = "Android", string file = "Test.graphql") =>
        _analyzer.ParseOperations(client, document, file);

    private IReadOnlyList<string> AffectedNames(ClassifiedSchemaChange change, IEnumerable<GraphQLOperation> operations) =>
        _analyzer.FindAffectedOperations(change, operations, Schema).Select(r => r.OperationName).ToList();

    // ---- Loading ----------------------------------------------------------------------------

    [Fact]
    public void LoadOperations_DemoAndroid_DiscoversAllGraphQLFiles()
    {
        var operations = _analyzer.LoadOperations(
            new ClientDefinition("Android", TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/android")));

        Assert.Equal(["GetCampaign", "GetCampaignDetails"], operations.Select(o => o.Name));
        Assert.All(operations, o => Assert.Equal("Android", o.ClientName));
        Assert.All(operations, o => Assert.EndsWith(".graphql", o.FilePath));
    }

    [Fact]
    public void LoadOperations_ScansRecursively_AndResolvesFragmentsFromOtherFiles()
    {
        var directory = Directory.CreateTempSubdirectory("schema-intelligence-");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory.FullName, "feature", "campaign"));
            File.WriteAllText(Path.Combine(directory.FullName, "fragments.graphql"), "fragment CampaignFields on Campaign { videoUrl }");
            File.WriteAllText(Path.Combine(directory.FullName, "feature", "campaign", "GetCampaign.gql"), "query GetCampaign { campaign { ...CampaignFields } }");
            File.WriteAllText(Path.Combine(directory.FullName, "feature", "notes.txt"), "query Ignored { user { id } }");

            var operations = _analyzer.LoadOperations(new ClientDefinition("iOS", directory.FullName));

            var operation = Assert.Single(operations);
            Assert.Equal("GetCampaign", operation.Name);
            Assert.Equal(["GetCampaign"], AffectedNames(VideoUrlRemoved, operations));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadOperations_MissingDirectory_ThrowsClearError()
    {
        var ex = Assert.Throws<OperationLoadException>(() =>
            _analyzer.LoadOperations(new ClientDefinition("Android", "does/not/exist")));

        Assert.Equal("Android GraphQL directory does not exist: does/not/exist", ex.Message);
    }

    [Fact]
    public void ParseOperations_InvalidGraphQL_ReportsFileLineAndColumn()
    {
        var ex = Assert.Throws<OperationLoadException>(() =>
            Parse("query GetCampaign {\n  campaign {\n    id(\n  }\n}", file: "GetCampaign.graphql"));

        Assert.StartsWith("Failed to parse operation:", ex.Message);
        Assert.Equal("GetCampaign.graphql", ex.FilePath);
        Assert.NotNull(ex.Line);
        Assert.NotNull(ex.Column);
    }

    [Fact]
    public void ParseOperations_UnknownFragment_ThrowsClearError()
    {
        var ex = Assert.Throws<OperationLoadException>(() => Parse("query Q { campaign { ...Missing } }"));

        Assert.Contains("unknown fragment 'Missing'", ex.Message);
    }

    // ---- Parsing ----------------------------------------------------------------------------

    [Fact]
    public void ParseOperations_ReadsNameAndType()
    {
        var operation = Assert.Single(Parse("query GetCampaign { campaign { id } }"));

        Assert.Equal("GetCampaign", operation.Name);
        Assert.Equal(GraphQLOperationType.Query, operation.OperationType);
        Assert.Equal("Test.graphql", operation.FilePath);
    }

    [Fact]
    public void ParseOperations_MutationSubscriptionAndAnonymousQuery()
    {
        var operations = Parse("""
            mutation Publish($input: CampaignInput!) { publishCampaign(input: $input) { id } }
            subscription OnUpdate { campaignUpdated { id } }
            { user { id } }
            """, file: "dir/Anonymous.graphql");

        Assert.Equal(
            ["Publish Mutation", "OnUpdate Subscription", "Anonymous Query"],
            operations.Select(o => $"{o.Name} {o.OperationType}"));
        Assert.Equal("input: CampaignInput!", $"{operations[0].Variables[0].Name}: {operations[0].Variables[0].Type}");
    }

    [Fact]
    public void GetFieldReferences_NestedFields_ResolveToSchemaCoordinates()
    {
        var operation = Assert.Single(Parse("query CampaignDetails { campaign { id title video { id url } } }"));

        var references = _analyzer.GetFieldReferences(operation, Schema);

        Assert.Equal(
            [
                "campaign → Query.campaign",
                "campaign.id → Campaign.id",
                "campaign.title → Campaign.title",
                "campaign.video → Campaign.video",
                "campaign.video.id → CampaignVideo.id",
                "campaign.video.url → CampaignVideo.url",
            ],
            references.Select(r => $"{r.Path} → {r.Coordinate}"));
    }

    [Fact]
    public void GetFieldReferences_IgnoresTypenameAndSkipsUnresolvableSubtrees()
    {
        var operation = Assert.Single(Parse("query Q { __typename campaign { __typename missing { id } id } }"));

        var references = _analyzer.GetFieldReferences(operation, Schema);

        // `missing` is kept (its parent type is known) but its children cannot be typed.
        Assert.Equal(["Query.campaign", "Campaign.missing", "Campaign.id"], references.Select(r => r.Coordinate));
        Assert.Null(references[1].ReturnType);
    }

    // ---- Field matching -------------------------------------------------------------------------

    [Fact]
    public void FindAffected_DirectFieldReference_IsAffected()
    {
        var affected = _analyzer.FindAffectedOperations(
            VideoUrlRemoved, Parse("query GetCampaign { campaign { videoUrl } }"), Schema);

        var reference = Assert.Single(affected);
        Assert.Equal("Android", reference.ClientName);
        Assert.Equal("GetCampaign", reference.OperationName);
        Assert.Equal(GraphQLOperationType.Query, reference.OperationType);
        Assert.Equal("Test.graphql", reference.FilePath);
        Assert.Equal("Campaign", reference.SchemaType);
        Assert.Equal("videoUrl", reference.FieldName);
        Assert.Equal(["campaign.videoUrl"], reference.Usages);
        Assert.Same(VideoUrlRemoved, reference.Change);
    }

    [Fact]
    public void FindAffected_UnrelatedOperation_IsNotAffected()
    {
        Assert.Empty(AffectedNames(VideoUrlRemoved, Parse("query GetUser { user { id name } }")));
    }

    [Fact]
    public void FindAffected_SameFieldNameOnOtherType_IsNotAffected()
    {
        // `id` exists on User too; only Campaign.id is changed.
        var change = Classified(ChangeType.FieldRemoved, "Campaign", "id");

        Assert.Empty(AffectedNames(change, Parse("query GetUser { user { id } }")));
    }

    [Fact]
    public void FindAffected_MultipleOperationsInOneFile_OnlyTheUsingOneIsReturned()
    {
        var operations = Parse("""
            query GetCampaign { campaign { id videoUrl } }
            query GetCampaignSummary { campaign { id } }
            """);

        Assert.Equal(["GetCampaign"], AffectedNames(VideoUrlRemoved, operations));
    }

    [Fact]
    public void FindAffected_NestedField_IsAffected()
    {
        var change = Classified(ChangeType.FieldRemoved, "CampaignVideo", "url");

        var affected = _analyzer.FindAffectedOperations(
            change, Parse("query CampaignDetails { campaign { video { url } } }"), Schema);

        Assert.Equal(["campaign.video.url"], Assert.Single(affected).Usages);
    }

    [Fact]
    public void FindAffected_FieldThroughFragment_IsAffected()
    {
        var operations = Parse("""
            fragment CampaignFields on Campaign { id title videoUrl }
            query GetCampaign { campaign { ...CampaignFields } }
            query GetCampaignCard { campaign { id } }
            """);

        var reference = Assert.Single(_analyzer.FindAffectedOperations(VideoUrlRemoved, operations, Schema));
        Assert.Equal("GetCampaign", reference.OperationName);
        Assert.Equal(["campaign.videoUrl (via fragment CampaignFields)"], reference.Usages);
    }

    [Fact]
    public void FindAffected_NestedFragmentsAndInlineFragments_AreFollowed()
    {
        var operations = Parse("""
            fragment VideoFields on Campaign { videoUrl }
            fragment CampaignFields on Campaign { id ...VideoFields }
            query ViaNestedFragment { campaign { ...CampaignFields } }
            query ViaInlineFragment { campaign { ... on Campaign { videoUrl } } }
            """);

        Assert.Equal(["ViaInlineFragment", "ViaNestedFragment"], AffectedNames(VideoUrlRemoved, operations));
    }

    [Fact]
    public void FindAffected_AliasedField_IsAffected()
    {
        var operations = Parse("query GetCampaign { campaign { link: videoUrl } }");

        var reference = _analyzer.GetFieldReferences(operations[0], Schema).Single(r => r.FieldName == "videoUrl");
        Assert.Equal("link", reference.Alias);
        Assert.Equal(["campaign.videoUrl (alias link)"], Assert.Single(_analyzer.FindAffectedOperations(VideoUrlRemoved, operations, Schema)).Usages);
    }

    [Fact]
    public void FindAffected_MutationAndSubscriptionFields_AreResolved()
    {
        var operations = Parse("""
            mutation Publish { publishCampaign(input: { title: "x" }) { videoUrl } }
            subscription OnUpdate { campaignUpdated { videoUrl } }
            """);

        Assert.Equal(["OnUpdate", "Publish"], AffectedNames(VideoUrlRemoved, operations));
    }

    [Fact]
    public void FindAffected_ArgumentChange_OnlyAffectsOperationsPassingTheArgument()
    {
        var change = Classified(ChangeType.ArgumentRemoved, "Query", "campaign", "id");
        var operations = Parse("""
            query WithArgument($id: ID) { campaign(id: $id) { id } }
            query WithoutArgument { campaign { id } }
            """);

        Assert.Equal(["WithArgument"], AffectedNames(change, operations));
    }

    [Fact]
    public void FindAffected_EnumValueChange_AffectsOperationsReadingOrPassingTheEnum()
    {
        var change = Classified(ChangeType.EnumValueRemoved, "CampaignStatus", "ACTIVE");
        var operations = Parse("""
            query ReadsStatus { campaign { status } }
            query FiltersByStatus { campaigns(status: ACTIVE) { id } }
            query DeclaresVariable($s: CampaignStatus) { user { id } }
            query Unrelated { campaign { id } }
            """);

        Assert.Equal(["DeclaresVariable", "FiltersByStatus", "ReadsStatus"], AffectedNames(change, operations));
    }

    [Fact]
    public void FindAffected_InputFieldChange_AffectsOperationsUsingTheInputType()
    {
        var change = Classified(ChangeType.InputFieldAdded, "CampaignInput", "slug");
        var operations = Parse("""
            mutation Publish($input: CampaignInput!) { publishCampaign(input: $input) { id } }
            query Unrelated { campaign { id } }
            """);

        Assert.Equal(["Publish"], AffectedNames(change, operations));
    }

    [Fact]
    public void FindAffected_TypeRemoved_AffectsOperationsSelectingThatType()
    {
        var change = Classified(ChangeType.TypeRemoved, "CampaignVideo");
        var operations = Parse("""
            query WithVideo { campaign { video { url } } }
            query WithoutVideo { campaign { videoUrl } }
            """);

        Assert.Equal(["WithVideo"], AffectedNames(change, operations));
    }

    [Fact]
    public void FindAffected_AndroidAndFrontend_AreReportedWithTheirClientNames()
    {
        var operations = _analyzer.LoadOperations(new ClientDefinition("Android", TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/android")))
            .Concat(_analyzer.LoadOperations(new ClientDefinition("Frontend", TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/frontend"))));

        var affected = _analyzer.FindAffectedOperations(VideoUrlRemoved, operations, Schema);

        Assert.Equal(
            ["Android/GetCampaign", "Android/GetCampaignDetails", "Frontend/CampaignDetails"],
            affected.Select(r => $"{r.ClientName}/{r.OperationName}"));
    }

    // ---- Full pipeline on the demo ---------------------------------------------------------------

    [Fact]
    public void DemoPipeline_IdentifiesAffectedAndUnaffectedOperations()
    {
        var run = TestReports.RunDemo();
        var operations = run.Operations;

        // Operations are written against the schema clients use today: the old one.
        var affectedByChange = run.Classified.ToDictionary(
            c => $"{c.Severity} {c.Change.ChangeType} {c.Change.Path}",
            c => _analyzer.FindAffectedOperations(c, operations, run.OldSchema).Select(r => $"{r.ClientName}/{r.OperationName}").ToList());

        Assert.Equal(
            ["Android/GetCampaign", "Android/GetCampaignDetails", "Frontend/CampaignDetails"],
            affectedByChange["Breaking FieldRemoved Campaign.videoUrl"]);
        Assert.Empty(affectedByChange["Info FieldAdded Campaign.video"]);
        Assert.Empty(affectedByChange["Info TypeAdded CampaignVideo"]);

        var affected = affectedByChange.Values.SelectMany(v => v).ToHashSet();
        Assert.Equal(
            ["Frontend/CampaignCard", "Frontend/GetUser"],
            operations.Select(o => $"{o.ClientName}/{o.Name}").Where(o => !affected.Contains(o)).Order());
    }
}
