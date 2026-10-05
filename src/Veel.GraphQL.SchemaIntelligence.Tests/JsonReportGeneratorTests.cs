using System.Text.Json;
using Veel.GraphQL.SchemaIntelligence.Reporting;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class JsonReportGeneratorTests
{
    private readonly JsonReportGenerator _generator = new();

    /// <summary>Generates and parses; the parse fails the test if the output is not valid JSON.</summary>
    private JsonElement GenerateAndParse(ChangeReport report) =>
        JsonDocument.Parse(_generator.Generate(report)).RootElement.Clone();

    private static JsonElement Change(JsonElement root, string path) =>
        root.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("path").GetString() == path);

    private static JsonElement ClientImpact(JsonElement root, string client) =>
        root.GetProperty("clientImpacts").EnumerateArray().Single(c => c.GetProperty("clientName").GetString() == client);

    private static IReadOnlyList<string?> Strings(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString()).ToList();

    // ---- Content ----------------------------------------------------------------------------

    [Fact]
    public void Generate_ProducesValidJsonWithContractVersion()
    {
        var root = GenerateAndParse(TestReports.DemoLike());

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(1, root.GetProperty("reportVersion").GetInt32());
        Assert.Equal("2026-10-05T10:00:00+00:00", root.GetProperty("generatedAt").GetString());
    }

    [Fact]
    public void ServiceName_ComesFromReport()
    {
        var root = GenerateAndParse(TestReports.DemoLike());

        Assert.Equal("Cerberus", root.GetProperty("serviceName").GetString());
    }

    [Fact]
    public void Summary_ContainsAllCounts()
    {
        var summary = GenerateAndParse(TestReports.DemoLike()).GetProperty("summary");

        Assert.Equal(3, summary.GetProperty("totalChanges").GetInt32());
        Assert.Equal(1, summary.GetProperty("breakingChanges").GetInt32());
        Assert.Equal(0, summary.GetProperty("warningChanges").GetInt32());
        Assert.Equal(2, summary.GetProperty("infoChanges").GetInt32());
        Assert.Equal(2, summary.GetProperty("affectedClients").GetInt32());
        Assert.Equal(3, summary.GetProperty("affectedOperations").GetInt32());
    }

    [Fact]
    public void BreakingChange_HasDomainValues()
    {
        var change = Change(GenerateAndParse(TestReports.DemoLike()), "Campaign.videoUrl");

        Assert.Equal("FieldRemoved", change.GetProperty("changeType").GetString());
        Assert.Equal("Breaking", change.GetProperty("severity").GetString());
        Assert.Equal("Campaign", change.GetProperty("schemaType").GetString());
        Assert.Equal("videoUrl", change.GetProperty("fieldName").GetString());
        Assert.Equal("String", change.GetProperty("oldType").GetString());
        Assert.Equal("An existing field was removed and clients using it may fail.", change.GetProperty("reason").GetString());
        Assert.True(change.GetProperty("hasClientImpact").GetBoolean());
    }

    [Fact]
    public void MeaningfulNulls_AreKeptAsJsonNull()
    {
        var root = GenerateAndParse(TestReports.DemoLike());

        Assert.Equal(JsonValueKind.Null, Change(root, "Campaign.videoUrl").GetProperty("newType").ValueKind);
        Assert.Equal(JsonValueKind.Null, Change(root, "Campaign.video").GetProperty("oldType").ValueKind);
        Assert.Equal("CampaignVideo", Change(root, "Campaign.video").GetProperty("newType").GetString());
        Assert.Equal(JsonValueKind.Null, Change(root, "CampaignVideo").GetProperty("fieldName").ValueKind);
    }

    [Fact]
    public void Enums_AreSerializedAsNames()
    {
        var json = _generator.Generate(TestReports.DemoLike());

        Assert.Contains("\"severity\": \"Breaking\"", json);
        Assert.Contains("\"changeType\": \"FieldRemoved\"", json);
        Assert.Contains("\"operationType\": \"Query\"", json);
        Assert.DoesNotContain("\"severity\": 2", json);
    }

    [Fact]
    public void ClientImpacts_ListAffectedOperationsPerClient()
    {
        var root = GenerateAndParse(TestReports.DemoLike());

        Assert.Equal(["Android", "Frontend"], Strings(root.GetProperty("scannedClients")));
        Assert.Equal(
            ["Android", "Frontend"],
            root.GetProperty("clientImpacts").EnumerateArray().Select(c => c.GetProperty("clientName").GetString()));

        var android = ClientImpact(root, "Android");
        Assert.Equal("Breaking", android.GetProperty("severity").GetString());
        Assert.Equal("Campaign", android.GetProperty("schemaType").GetString());
        Assert.Equal("videoUrl", android.GetProperty("fieldName").GetString());
        Assert.Equal(["GetCampaign", "GetCampaignDetails"], Strings(android.GetProperty("affectedOperations")));
        Assert.Equal(["campaign.videoUrl"], Strings(android.GetProperty("operations")[0].GetProperty("usages")));

        Assert.Equal(["CampaignDetails"], Strings(ClientImpact(root, "Frontend").GetProperty("affectedOperations")));
    }

    [Fact]
    public void PossibleMigration_IsPreservedAsUnverifiedCandidate()
    {
        var migrations = Change(GenerateAndParse(TestReports.DemoLike()), "Campaign.videoUrl").GetProperty("possibleMigrations");

        var migration = Assert.Single(migrations.EnumerateArray());
        Assert.Equal("Campaign.videoUrl", migration.GetProperty("from").GetString());
        Assert.Equal("Campaign.video", migration.GetProperty("to").GetString());
        Assert.Equal("Possible", migration.GetProperty("confidence").GetString());
        Assert.True(migration.GetProperty("developerVerificationRequired").GetBoolean());
    }

    // ---- Empty and partial reports ------------------------------------------------------------

    [Fact]
    public void NoChanges_ProducesEmptyArraysNotNulls()
    {
        var root = GenerateAndParse(TestReports.Build(TestReports.OldSchema, TestReports.OldSchema, ("Android", "query GetUser { user { id } }")));

        Assert.Equal(0, root.GetProperty("summary").GetProperty("totalChanges").GetInt32());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("affectedOperations").GetInt32());
        Assert.Equal(0, root.GetProperty("changes").GetArrayLength());
        Assert.Equal(0, root.GetProperty("clientImpacts").GetArrayLength());
        Assert.Equal(0, root.GetProperty("summary").GetProperty("clients").GetArrayLength());
        Assert.Equal(1, root.GetProperty("unaffectedOperations").GetArrayLength());
    }

    [Fact]
    public void BreakingChangeWithoutAffectedClients_StaysBreaking()
    {
        var root = GenerateAndParse(TestReports.Build(
            "type Query { a: String b: String }", "type Query { a: String }", ("Android", "query A { a }")));

        var summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("breakingChanges").GetInt32());
        Assert.Equal(0, summary.GetProperty("affectedClients").GetInt32());
        Assert.Equal(0, summary.GetProperty("affectedOperations").GetInt32());
        Assert.Equal("Breaking", Change(root, "Query.b").GetProperty("severity").GetString());
        Assert.False(Change(root, "Query.b").GetProperty("hasClientImpact").GetBoolean());
        Assert.Equal(0, root.GetProperty("clientImpacts").GetArrayLength());
    }

    // ---- Determinism and consistency ---------------------------------------------------------

    [Fact]
    public void SameReport_ProducesIdenticalJson()
    {
        var report = TestReports.DemoLike();

        var first = _generator.Generate(report);
        var second = _generator.Generate(report);

        Assert.Equal(first, second);
        Assert.Equal(first, _generator.Generate(TestReports.DemoLike()));
        Assert.DoesNotContain("\r", first);
    }

    [Fact]
    public void MarkdownAndJson_FromSameReport_AgreeOnKeyData()
    {
        var report = TestReports.FromDemoFiles();
        var markdown = new MarkdownReportGenerator().Generate(report);
        var root = GenerateAndParse(report);

        // Summary counts
        var summary = root.GetProperty("summary");
        var summaryTable = TestReports.MarkdownSection(markdown, "## Summary");
        Assert.Contains($"| Total Changes | {summary.GetProperty("totalChanges").GetInt32()} |", summaryTable);
        Assert.Contains($"| 🔴 Breaking | {summary.GetProperty("breakingChanges").GetInt32()} |", summaryTable);
        Assert.Contains($"| 🟡 Warning | {summary.GetProperty("warningChanges").GetInt32()} |", summaryTable);
        Assert.Contains($"| 🟢 Informational | {summary.GetProperty("infoChanges").GetInt32()} |", summaryTable);
        Assert.Contains($"| Affected Clients | {summary.GetProperty("affectedClients").GetInt32()} |", summaryTable);
        Assert.Contains($"| Affected Operations | {summary.GetProperty("affectedOperations").GetInt32()} |", summaryTable);

        // Every change appears in the severity section matching its JSON severity, with the same change type.
        var sections = new Dictionary<string, string>
        {
            ["Breaking"] = "## 🔴 Breaking Changes",
            ["Warning"] = "## 🟡 Warnings",
            ["Info"] = "## 🟢 Informational Changes",
        };
        foreach (var change in root.GetProperty("changes").EnumerateArray())
        {
            var section = TestReports.MarkdownSection(markdown, sections[change.GetProperty("severity").GetString()!]);
            var path = change.GetProperty("path").GetString();
            Assert.Contains($"### `{path}`", section);
            Assert.Equal($"{change.GetProperty("schemaType").GetString()}{(change.GetProperty("fieldName").GetString() is { } f ? "." + f : "")}", path);
        }

        // Every affected operation appears under its client, against the same schema element.
        var clientImpact = TestReports.MarkdownSection(markdown, "## Client Impact");
        foreach (var impact in root.GetProperty("clientImpacts").EnumerateArray())
        {
            Assert.Contains($"### {(impact.GetProperty("clientName").GetString() == "Android" ? "📱" : "🌐")} {impact.GetProperty("clientName").GetString()}", clientImpact);
            foreach (var operation in impact.GetProperty("affectedOperations").EnumerateArray())
            {
                Assert.Contains($"- `{operation.GetString()}` — `{impact.GetProperty("path").GetString()}`", clientImpact);
            }
        }
    }

    // ---- Full pipeline on the demo ---------------------------------------------------------------

    [Fact]
    public void DemoPipeline_JsonIdentifiesChangesAndAffectedOperations()
    {
        var root = GenerateAndParse(TestReports.FromDemoFiles());

        Assert.Equal(
            ["FieldRemoved Breaking Campaign.videoUrl", "FieldAdded Info Campaign.video", "TypeAdded Info CampaignVideo"],
            root.GetProperty("changes").EnumerateArray().Select(c =>
                $"{c.GetProperty("changeType").GetString()} {c.GetProperty("severity").GetString()} {c.GetProperty("path").GetString()}"));

        Assert.Equal(["GetCampaign", "GetCampaignDetails"], Strings(ClientImpact(root, "Android").GetProperty("affectedOperations")));
        Assert.Equal(["CampaignDetails"], Strings(ClientImpact(root, "Frontend").GetProperty("affectedOperations")));
        Assert.Equal(
            ["CampaignCard", "GetUser"],
            root.GetProperty("unaffectedOperations").EnumerateArray().Select(o => o.GetProperty("operationName").GetString()));
    }
}
