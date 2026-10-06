using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Impact;
using Veel.GraphQL.SchemaIntelligence.Reporting;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

/// <summary>Builds <see cref="ChangeReport"/>s through the real pipeline, with a fixed timestamp, for report tests.</summary>
internal static class TestReports
{
    public static readonly DateTimeOffset FixedTime = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    public const string OldSchema = """
        type Campaign { id: ID! title: String! videoUrl: String }
        type User { id: ID! name: String! }
        type Query { campaign: Campaign, user: User }
        """;

    public const string NewSchema = """
        type Campaign { id: ID! title: String! video: CampaignVideo }
        type CampaignVideo { id: ID! url: String! }
        type User { id: ID! name: String! }
        type Query { campaign: Campaign, user: User }
        """;

    /// <summary>Runs the pipeline on inline SDL and operations (one document per client).</summary>
    public static ChangeReport Build(string oldSdl, string newSdl, params (string Client, string Document)[] clientDocuments)
    {
        var oldSchema = SchemaLoader.Parse(oldSdl);
        var classified = new ChangeClassifier().Classify(new SchemaDiffer().Compare(oldSchema, SchemaLoader.Parse(newSdl)));

        var operationAnalyzer = new OperationAnalyzer();
        var operations = clientDocuments
            .SelectMany(d => operationAnalyzer.ParseOperations(d.Client, d.Document, $"{d.Client.ToLowerInvariant()}/ops.graphql"))
            .ToList();
        var references = classified.SelectMany(c => operationAnalyzer.FindAffectedOperations(c, operations, oldSchema));
        var analysis = new ImpactAnalyzer().Analyze(classified, references);

        var clients = clientDocuments.Select(d => new ClientDefinition(d.Client, d.Client.ToLowerInvariant())).Distinct();
        return ChangeReport.Create("Cerberus", FixedTime, analysis, clients, operations);
    }

    /// <summary>The demo scenario, written inline.</summary>
    public static ChangeReport DemoLike() => Build(
        OldSchema,
        NewSchema,
        ("Android", "query GetCampaign { campaign { id videoUrl } } query GetCampaignDetails { campaign { title videoUrl } }"),
        ("Frontend", "query CampaignDetails { campaign { videoUrl } } query CampaignCard { campaign { id title } }"));

    /// <summary>The demo scenario's report, built from the fixed copies in <see cref="TestPaths.Fixtures"/>.</summary>
    public static ChangeReport FromDemoFiles() => RunDemo().Report;

    /// <summary>
    /// Runs the full pipeline exactly as the CLI does, on the fixture clients and the given schema files
    /// (relative to the repository root), exposing every intermediate result.
    /// </summary>
    public static PipelineRun RunDemo(string oldSchemaFile = $"{TestPaths.Fixtures}/old-schema.graphql", string newSchemaFile = $"{TestPaths.Fixtures}/new-schema.graphql")
    {
        var oldSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot(oldSchemaFile));
        var newSchema = SchemaLoader.LoadFromFile(TestPaths.FromRepositoryRoot(newSchemaFile));
        var classified = new ChangeClassifier().Classify(new SchemaDiffer().Compare(oldSchema, newSchema));

        ClientDefinition[] clients =
        [
            new("Android", TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/android")),
            new("Frontend", TestPaths.FromRepositoryRoot($"{TestPaths.Fixtures}/frontend")),
        ];
        var operationAnalyzer = new OperationAnalyzer();
        var operations = clients.SelectMany(operationAnalyzer.LoadOperations).ToList();

        // Operations are written against the schema clients use today: the old one.
        var references = classified.SelectMany(c => operationAnalyzer.FindAffectedOperations(c, operations, oldSchema)).ToList();
        var analysis = new ImpactAnalyzer().Analyze(classified, references);
        var report = ChangeReport.Create("Cerberus", FixedTime, analysis, clients, operations);

        return new PipelineRun(oldSchema, newSchema, classified, clients, operations, references, analysis, report);
    }

    /// <summary>The text of a Markdown <c>## </c> section, up to the next <c>## </c> heading.</summary>
    public static string MarkdownSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Section '{heading}' not found");
        var end = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? markdown[start..] : markdown[start..end];
    }
}

/// <summary>Every stage of one pipeline run.</summary>
internal sealed record PipelineRun(
    GraphQLSchema OldSchema,
    GraphQLSchema NewSchema,
    IReadOnlyList<ClassifiedSchemaChange> Classified,
    IReadOnlyList<ClientDefinition> Clients,
    IReadOnlyList<GraphQLOperation> Operations,
    IReadOnlyList<ClientOperationReference> References,
    ImpactAnalysis Analysis,
    ChangeReport Report);
