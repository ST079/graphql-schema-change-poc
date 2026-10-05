using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Impact;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class ImpactAnalyzerTests
{
    private readonly ImpactAnalyzer _analyzer = new();

    private static TypeReference Named(string name) => new NamedTypeReference(name);
    private static TypeReference NonNull(string name) => new NonNullTypeReference(new NamedTypeReference(name));

    private static ClassifiedSchemaChange Classified(
        ChangeType changeType,
        string typeName,
        string? field = null,
        string? argument = null,
        TypeReference? oldType = null,
        TypeReference? newType = null,
        string? newValue = null) =>
        new ChangeClassifier().Classify(new SchemaChange(
            changeType, typeName, field, argument, oldType, newType, null, newValue, $"{changeType} {typeName}.{field}."));

    private static readonly ClassifiedSchemaChange VideoUrlRemoved =
        Classified(ChangeType.FieldRemoved, "Campaign", "videoUrl", oldType: Named("String"));
    private static readonly ClassifiedSchemaChange VideoAdded =
        Classified(ChangeType.FieldAdded, "Campaign", "video", newType: Named("CampaignVideo"));
    private static readonly ClassifiedSchemaChange CampaignVideoAdded =
        Classified(ChangeType.TypeAdded, "CampaignVideo");

    private static ClientOperationReference Ref(string client, string operation, ClassifiedSchemaChange change) =>
        new(client, operation, GraphQLOperationType.Query, $"{client.ToLowerInvariant()}/{operation}.graphql",
            change.Change.TypeName, change.Change.FieldName, [$"campaign.{change.Change.FieldName}"], change);

    /// <summary>Compact rendering of an analysis for order-sensitive assertions.</summary>
    private static IReadOnlyList<string> Render(ImpactAnalysis analysis) =>
        analysis.Changes
            .Select(c => $"{c.Severity} {c.Change.Change.ChangeType} {c.Change.Change.Path}: " +
                         string.Join("; ", c.ClientImpacts.Select(i => $"{i.ClientName}[{string.Join(",", i.AffectedOperationNames)}]")))
            .ToList();

    // ---- Client impacts -------------------------------------------------------------------------

    [Fact]
    public void BreakingFieldRemoval_ProducesOneImpactPerClient()
    {
        var analysis = _analyzer.Analyze(
            [VideoUrlRemoved],
            [Ref("Android", "GetCampaign", VideoUrlRemoved), Ref("Frontend", "CampaignDetails", VideoUrlRemoved)]);

        var impact = Assert.Single(analysis.Changes);
        Assert.True(impact.HasClientImpact);
        Assert.Equal(["Android", "Frontend"], impact.ClientImpacts.Select(c => c.ClientName));
        Assert.Equal(2, analysis.ClientImpacts.Count);
    }

    [Fact]
    public void OperationsOfOneClient_AreGroupedIntoOneSortedImpact()
    {
        var analysis = _analyzer.Analyze(
            [VideoUrlRemoved],
            [
                Ref("Android", "SearchCampaigns", VideoUrlRemoved),
                Ref("Android", "GetCampaign", VideoUrlRemoved),
                Ref("Android", "GetCampaignDetails", VideoUrlRemoved),
            ]);

        var clientImpact = Assert.Single(analysis.ClientImpacts);
        Assert.Equal("Android", clientImpact.ClientName);
        Assert.Equal(["GetCampaign", "GetCampaignDetails", "SearchCampaigns"], clientImpact.AffectedOperationNames);
    }

    [Fact]
    public void ClientWithoutMatchingOperation_GetsNoImpact()
    {
        // Real operations, so the only references are the ones the operation analyzer actually finds.
        var schema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot("demo/old-schema.graphql"));
        var operationAnalyzer = new OperationAnalyzer();
        var operations = operationAnalyzer.ParseOperations("Android", "query GetUser { user { id name } }")
            .Concat(operationAnalyzer.ParseOperations("Frontend", "query CampaignDetails { campaign { id videoUrl } }"));

        var analysis = _analyzer.Analyze(
            [VideoUrlRemoved], operationAnalyzer.FindAffectedOperations(VideoUrlRemoved, operations, schema));

        var clientImpact = Assert.Single(analysis.ClientImpacts);
        Assert.Equal("Frontend", clientImpact.ClientName);
        Assert.Equal(["CampaignDetails"], clientImpact.AffectedOperationNames);
    }

    [Fact]
    public void ChangeWithoutClientUsage_HasNoClientImpacts()
    {
        var analyticsAdded = Classified(ChangeType.TypeAdded, "CampaignAnalytics");

        var impact = Assert.Single(_analyzer.Analyze([analyticsAdded], []).Changes);

        Assert.False(impact.HasClientImpact);
        Assert.Empty(impact.ClientImpacts);
    }

    [Fact]
    public void BreakingChangeWithoutClientUsage_StaysBreaking()
    {
        var analysis = _analyzer.Analyze([VideoUrlRemoved], []);

        Assert.Equal(ChangeSeverity.Breaking, analysis.Changes[0].Severity);
        Assert.False(analysis.Changes[0].HasClientImpact);
        Assert.Equal(1, analysis.Summary.BreakingChanges);
        Assert.Empty(analysis.Summary.Clients);
    }

    [Fact]
    public void ClientImpact_PreservesSeverityAndChangeDetails()
    {
        var analysis = _analyzer.Analyze([VideoUrlRemoved], [Ref("Android", "GetCampaign", VideoUrlRemoved)]);

        var clientImpact = Assert.Single(analysis.ClientImpacts);
        Assert.Same(VideoUrlRemoved, clientImpact.Change);
        Assert.Equal(ChangeSeverity.Breaking, clientImpact.Severity);
        Assert.Equal(ChangeType.FieldRemoved, clientImpact.ChangeType);
        Assert.Equal("Campaign", clientImpact.SchemaType);
        Assert.Equal("videoUrl", clientImpact.FieldName);
        Assert.Equal("String", clientImpact.OldType!.ToString());
        Assert.Null(clientImpact.NewType);
        Assert.Equal("FieldRemoved Campaign.videoUrl. 1 Android operation(s) use it.", clientImpact.Reason);
    }

    // ---- Recommendations -----------------------------------------------------------------------

    [Fact]
    public void FieldRemoved_WithoutCandidate_RecommendsGenericAction()
    {
        var analysis = _analyzer.Analyze([VideoUrlRemoved], [Ref("Android", "GetCampaign", VideoUrlRemoved)]);

        Assert.Equal("Update affected client operations to stop requesting Campaign.videoUrl.", analysis.ClientImpacts[0].RecommendedAction);
        Assert.Empty(analysis.Changes[0].PossibleMigrations);
    }

    [Fact]
    public void FieldRemoved_WithSimilarAddedField_SuggestsPossibleMigrationOnly()
    {
        var analysis = _analyzer.Analyze([VideoUrlRemoved, VideoAdded], []);

        var removal = analysis.Changes.Single(c => c.Change == VideoUrlRemoved);
        Assert.Equal(["Campaign.video"], removal.PossibleMigrations);
        // The candidate is data, not part of the action text: outputs label it as needing verification.
        Assert.Equal("Update affected client operations to stop requesting Campaign.videoUrl.", removal.RecommendedAction);
    }

    [Theory]
    [InlineData("Campaign", "title")]         // unrelated name
    [InlineData("CampaignVideo", "video")]    // similar name, different type
    [InlineData("Campaign", "vi")]            // too short to be meaningful
    public void FieldRemoved_UnrelatedAddedField_IsNotACandidate(string typeName, string addedField)
    {
        var added = Classified(ChangeType.FieldAdded, typeName, addedField, newType: Named("String"));

        var analysis = _analyzer.Analyze([VideoUrlRemoved, added], []);

        Assert.Empty(analysis.Changes.Single(c => c.Change == VideoUrlRemoved).PossibleMigrations);
    }

    [Fact]
    public void FieldTypeChanged_ToDifferentType_IsBreakingWithModelUpdateAction()
    {
        var change = Classified(ChangeType.FieldTypeChanged, "Campaign", "title", oldType: Named("String"), newType: Named("Int"));

        var clientImpact = Assert.Single(_analyzer.Analyze([change], [Ref("Android", "GetCampaign", change)]).ClientImpacts);

        Assert.Equal(ChangeSeverity.Breaking, clientImpact.Severity);
        Assert.Equal(
            "Update the affected client operation and corresponding client model to handle the new field type.",
            clientImpact.RecommendedAction);
    }

    [Theory]
    [InlineData("String", "String!")]
    [InlineData("[String]", "[String!]!")]
    public void FieldTypeChanged_NullabilityOnly_RecommendsReview(string oldType, string newType)
    {
        var change = new ChangeClassifier().Classify(new SchemaDiffer().Compare(
            SchemaLoader.Parse($"type Query {{ f: {oldType} }}"),
            SchemaLoader.Parse($"type Query {{ f: {newType} }}")).Single());

        var impact = Assert.Single(_analyzer.Analyze([change], []).Changes);

        Assert.Equal(ChangeSeverity.Breaking, impact.Severity);
        Assert.Equal("Review affected client operations and update generated/client models if required.", impact.RecommendedAction);
    }

    [Theory]
    [InlineData(ChangeType.FieldAdded, "No action required for existing clients unless they want to use the new field.")]
    [InlineData(ChangeType.TypeAdded, "No action required for existing clients unless they need the new GraphQL type.")]
    [InlineData(ChangeType.TypeRemoved, "Remove usage of the deleted GraphQL type from affected client operations and client models.")]
    [InlineData(ChangeType.EnumValueRemoved, "Update affected client code to handle the removed enum value and verify enum handling.")]
    [InlineData(ChangeType.EnumValueAdded, "Review enum handling if the client assumes a fixed set of enum values.")]
    public void SpecifiedRecommendations_UseExactWording(ChangeType changeType, string expected)
    {
        var change = Classified(changeType, "Campaign", "x", newType: Named("String"));

        Assert.Equal(expected, Assert.Single(_analyzer.Analyze([change], []).Changes).RecommendedAction);
    }

    [Fact]
    public void EveryChangeType_HasARecommendation()
    {
        foreach (var changeType in Enum.GetValues<ChangeType>())
        {
            var change = Classified(changeType, "Campaign", "f", "arg", Named("String"), Named("Int"), "reason");

            var impact = Assert.Single(_analyzer.Analyze([change], []).Changes);
            Assert.False(string.IsNullOrWhiteSpace(impact.RecommendedAction), $"{changeType} has no recommendation");
        }
    }

    // ---- Multiple changes, ordering and summary ---------------------------------------------------

    [Fact]
    public void MultipleChanges_StaySeparate_AndAreOrderedBySeverityThenLocation()
    {
        var analysis = _analyzer.Analyze(
            [CampaignVideoAdded, VideoAdded, VideoUrlRemoved],
            [Ref("Frontend", "CampaignDetails", VideoUrlRemoved), Ref("Android", "GetCampaign", VideoUrlRemoved)]);

        Assert.Equal(
            [
                "Breaking FieldRemoved Campaign.videoUrl: Android[GetCampaign]; Frontend[CampaignDetails]",
                "Info FieldAdded Campaign.video: ",
                "Info TypeAdded CampaignVideo: ",
            ],
            Render(analysis));
    }

    [Fact]
    public void Analyze_IsDeterministic_RegardlessOfInputOrder()
    {
        ClientOperationReference[] references =
        [
            Ref("Frontend", "CampaignDetails", VideoUrlRemoved),
            Ref("Android", "GetCampaignDetails", VideoUrlRemoved),
            Ref("Android", "GetCampaign", VideoUrlRemoved),
        ];

        var first = _analyzer.Analyze([VideoUrlRemoved, VideoAdded, CampaignVideoAdded], references);
        var second = _analyzer.Analyze([CampaignVideoAdded, VideoUrlRemoved, VideoAdded], references.Reverse());

        Assert.Equal(Render(first), Render(second));
        Assert.Equal(first.Summary.Clients, second.Summary.Clients);
    }

    [Fact]
    public void Summary_CountsChangesClientsAndDistinctOperations()
    {
        var deprecated = Classified(ChangeType.DeprecationAdded, "Campaign", "title", newValue: "Use name");
        var analysis = _analyzer.Analyze(
            [VideoUrlRemoved, VideoAdded, deprecated],
            [
                Ref("Android", "GetCampaign", VideoUrlRemoved),
                Ref("Android", "GetCampaignDetails", VideoUrlRemoved),
                Ref("Android", "GetCampaign", deprecated),     // same operation, second change: counted once
                Ref("Frontend", "CampaignCard", deprecated),
            ]);

        var summary = analysis.Summary;
        Assert.Equal((3, 1, 1, 1), (summary.TotalChanges, summary.BreakingChanges, summary.WarningChanges, summary.InfoChanges));
        Assert.Equal(["Android", "Frontend"], summary.AffectedClients);
        Assert.Equal(
            [new ClientImpactSummary("Android", 2, ChangeSeverity.Breaking), new ClientImpactSummary("Frontend", 1, ChangeSeverity.Warning)],
            summary.Clients);
    }

    // ---- Error handling ------------------------------------------------------------------------

    [Fact]
    public void ReferenceToUnknownChange_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _analyzer.Analyze([VideoAdded], [Ref("Android", "GetCampaign", VideoUrlRemoved)]));

        Assert.Contains("Android/GetCampaign", ex.Message);
    }

    [Fact]
    public void ChangeMissingRequiredData_Throws()
    {
        var incomplete = Classified(ChangeType.FieldRemoved, "Campaign", field: null);

        var ex = Assert.Throws<InvalidOperationException>(() => _analyzer.Analyze([incomplete], []));

        Assert.Equal("FieldRemoved change on 'Campaign' is missing its field name.", ex.Message);
    }

    [Fact]
    public void ReferenceWithoutClientName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _analyzer.Analyze([VideoUrlRemoved], [Ref("", "GetCampaign", VideoUrlRemoved)]));
    }

    // ---- Full pipeline on the demo ---------------------------------------------------------------

    [Fact]
    public void DemoPipeline_ProducesExpectedClientImpacts()
    {
        var run = TestReports.RunDemo();

        var analysis = _analyzer.Analyze(run.Classified, run.References);

        Assert.Equal(
            [
                "Breaking FieldRemoved Campaign.videoUrl: Android[GetCampaign,GetCampaignDetails]; Frontend[CampaignDetails]",
                "Info FieldAdded Campaign.video: ",
                "Info TypeAdded CampaignVideo: ",
            ],
            Render(analysis));

        // video is offered as a candidate to verify, never merged into the removal as a rename.
        var removal = analysis.Changes[0];
        Assert.Equal(["Campaign.video"], removal.PossibleMigrations);
        Assert.Equal("Update affected client operations to stop requesting Campaign.videoUrl.", removal.RecommendedAction);
        Assert.Equal(3, analysis.Changes.Count);

        Assert.Equal(
            [new ClientImpactSummary("Android", 2, ChangeSeverity.Breaking), new ClientImpactSummary("Frontend", 1, ChangeSeverity.Breaking)],
            analysis.Summary.Clients);
        Assert.Equal((3, 1, 0, 2), (analysis.Summary.TotalChanges, analysis.Summary.BreakingChanges, analysis.Summary.WarningChanges, analysis.Summary.InfoChanges));
    }
}
