using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Impact;
using Veel.GraphQL.SchemaIntelligence.Reporting;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class ReportGeneratorTests
{
    private const string OldSchema = TestReports.OldSchema;
    private const string NewSchema = TestReports.NewSchema;
    private static readonly DateTimeOffset FixedTime = TestReports.FixedTime;

    private readonly MarkdownReportGenerator _generator = new();

    private static ChangeReport BuildReport(string oldSdl, string newSdl, params (string Client, string Document)[] clientDocuments) =>
        TestReports.Build(oldSdl, newSdl, clientDocuments);

    private static ChangeReport DemoLikeReport() => TestReports.DemoLike();

    private static string Section(string markdown, string heading) => TestReports.MarkdownSection(markdown, heading);

    // ---- Content ----------------------------------------------------------------------------

    [Fact]
    public void BasicReport_HasTitleAndServiceFromReport()
    {
        var markdown = _generator.Generate(DemoLikeReport());

        Assert.StartsWith("# 🚨 GraphQL Schema Change Report\n", markdown);
        Assert.Contains("**Service:** Cerberus\n", markdown);
        Assert.Contains("**Generated:** 2026-10-05 10:00 UTC\n", markdown);
    }

    [Fact]
    public void Summary_ShowsCountsFromReport()
    {
        var summary = Section(_generator.Generate(DemoLikeReport()), "## Summary");

        Assert.Contains("| Total Changes | 3 |", summary);
        Assert.Contains("| 🔴 Breaking | 1 |", summary);
        Assert.Contains("| 🟡 Warning | 0 |", summary);
        Assert.Contains("| 🟢 Informational | 2 |", summary);
        Assert.Contains("| Affected Clients | 2 |", summary);
        Assert.Contains("| Affected Operations | 3 |", summary);
    }

    [Fact]
    public void ClientImpact_ListsAffectedAndroidOperations()
    {
        var clientImpact = Section(_generator.Generate(DemoLikeReport()), "## Client Impact");

        Assert.Contains("### 📱 Android", clientImpact);
        Assert.Contains("- `GetCampaign` — `Campaign.videoUrl` (🔴 Breaking)", clientImpact);
        Assert.Contains("- `GetCampaignDetails` — `Campaign.videoUrl` (🔴 Breaking)", clientImpact);
    }

    [Fact]
    public void ClientImpact_ListsAffectedFrontendOperations_AndOmitsUnaffectedOnes()
    {
        var clientImpact = Section(_generator.Generate(DemoLikeReport()), "## Client Impact");

        Assert.Contains("### 🌐 Frontend", clientImpact);
        Assert.Contains("- `CampaignDetails` — `Campaign.videoUrl` (🔴 Breaking)", clientImpact);
        Assert.DoesNotContain("CampaignCard", clientImpact);
    }

    [Fact]
    public void ClientWithoutAffectedOperations_IsOmittedFromClientImpact_ButListedAsUnaffected()
    {
        var markdown = _generator.Generate(BuildReport(
            OldSchema, NewSchema,
            ("Android", "query GetUser { user { id name } }"),
            ("Frontend", "query CampaignDetails { campaign { videoUrl } }")));

        Assert.DoesNotContain("Android", Section(markdown, "## Client Impact"));
        Assert.DoesNotContain("Android", Section(markdown, "## 🔴 Breaking Changes"));
        Assert.Contains("**Android**\n- `GetUser`", Section(markdown, "## ✅ Unaffected Operations"));
    }

    [Fact]
    public void BreakingChangeWithoutClientUsage_IsStatedExplicitly()
    {
        var markdown = _generator.Generate(BuildReport(OldSchema, NewSchema, ("Android", "query GetUser { user { id } }")));

        Assert.Contains("No currently detected client operations are affected.", Section(markdown, "## 🔴 Breaking Changes"));
        Assert.Contains("No currently detected client operations are affected.", Section(markdown, "## Client Impact"));
        Assert.DoesNotContain("## Recommendation", markdown);
    }

    [Fact]
    public void NoClientsScanned_SaysSoInsteadOfClaimingNoImpact()
    {
        var markdown = _generator.Generate(BuildReport(OldSchema, NewSchema));

        Assert.Contains("No client operations were analyzed.", Section(markdown, "## Client Impact"));
        Assert.DoesNotContain("## ✅ Unaffected Operations", markdown);
    }

    [Fact]
    public void PossibleMigration_IsLabelledAsCandidateNeedingVerification()
    {
        var breaking = Section(_generator.Generate(DemoLikeReport()), "## 🔴 Breaking Changes");

        Assert.Contains("Update affected client operations to stop requesting `Campaign.videoUrl`.", breaking);
        Assert.Contains(
            "> **Possible migration candidate:**  \n> `Campaign.videoUrl` → `Campaign.video`  \n>\n> Developer verification required.",
            breaking);
        Assert.DoesNotContain("renamed", breaking, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangesTable_HasOneRowPerChange()
    {
        var table = Section(_generator.Generate(DemoLikeReport()), "## Changes");

        Assert.Contains("| 🔴 Breaking | Field removed | `Campaign.videoUrl` | `String` | — |", table);
        Assert.Contains("| 🟢 Info | Field added | `Campaign.video` | — | `CampaignVideo` |", table);
        Assert.Contains("| 🟢 Info | Type added | `CampaignVideo` | — | — |", table);
    }

    [Fact]
    public void Warnings_AreRenderedInTheirOwnSection()
    {
        var markdown = _generator.Generate(BuildReport(
            "type Query { title: String }",
            "type Query { title: String @deprecated(reason: \"Use name\") }",
            ("Android", "query GetTitle { title }")));

        var warnings = Section(markdown, "## 🟡 Warnings");
        Assert.Contains("**Change:** Deprecation added", warnings);
        Assert.Contains("**Deprecation reason:** Use name", warnings);
        Assert.Contains("**Android**\n- `GetTitle`", warnings);
        Assert.DoesNotContain("## 🔴 Breaking Changes", markdown);
        Assert.StartsWith("# GraphQL Schema Change Report\n", markdown);
    }

    // ---- Empty and partial reports ------------------------------------------------------------

    [Fact]
    public void NoChanges_ProducesMinimalReport()
    {
        var markdown = _generator.Generate(BuildReport(OldSchema, OldSchema, ("Android", "query GetUser { user { id } }")));

        Assert.Equal(
            "# GraphQL Schema Change Report\n\n" +
            "**Service:** Cerberus\n\n" +
            "**Generated:** 2026-10-05 10:00 UTC\n\n" +
            "## Summary\n\n" +
            "No schema changes detected.\n",
            markdown);
    }

    [Fact]
    public void OnlyBreakingChanges_HasNoWarningOrInfoSections()
    {
        var markdown = _generator.Generate(BuildReport(
            "type Query { a: String b: String }",
            "type Query { a: String }"));

        Assert.Contains("## 🔴 Breaking Changes", markdown);
        Assert.DoesNotContain("## 🟡 Warnings", markdown);
        Assert.DoesNotContain("## 🟢 Informational Changes", markdown);
    }

    [Fact]
    public void OnlyInformationalChanges_HasNoBreakingSection()
    {
        var markdown = _generator.Generate(BuildReport(
            "type Query { a: String }",
            "type Query { a: String b: String }",
            ("Android", "query A { a }")));

        Assert.StartsWith("# GraphQL Schema Change Report\n", markdown);
        Assert.Contains("## 🟢 Informational Changes", markdown);
        Assert.DoesNotContain("## 🔴 Breaking Changes", markdown);
        Assert.DoesNotContain("## 🟡 Warnings", markdown);
        Assert.DoesNotContain("## Recommendation", markdown);
    }

    // ---- Escaping and determinism ------------------------------------------------------------

    [Fact]
    public void MarkdownSensitiveCharacters_AreEscapedOrCodeFormatted()
    {
        var report = BuildReport(
            "type Query { video_url: String keep: String }",
            "type Query { keep: String }",
            ("Android", "query Get_Video { video_url }"))
            with { ServiceName = "Cerberus_v2 | *beta* [x]" };

        var markdown = _generator.Generate(report);

        Assert.Contains(@"**Service:** Cerberus\_v2 \| \*beta\* \[x\]", markdown);
        Assert.Contains("### `Query.video_url`", markdown);
        Assert.Contains("stop requesting `Query.video_url`.", markdown);
        Assert.Contains("| 🔴 Breaking | Field removed | `Query.video_url` | `String` | — |", markdown);
        Assert.Contains("- `Get_Video`", markdown);
    }

    [Fact]
    public void RecommendedAction_CodeFormatsOnlyWholeOccurrencesOfThePath()
    {
        // Regression: `Query.title` used to be code-formatted inside `Query.titleText`, giving "`Query.title`Text".
        var markdown = _generator.Generate(BuildReport(
            "type Query { title: String titleText: String }",
            "type Query { title: String @deprecated(reason: \"Use Query.titleText\") titleText: String }"));

        Assert.Contains(
            "Plan migration away from `Query.title` before it is removed. Deprecation reason: Use Query.titleText",
            markdown);
        Assert.DoesNotContain("`Query.title`Text", markdown);
    }

    [Fact]
    public void SameReport_ProducesIdenticalMarkdown()
    {
        var first = _generator.Generate(DemoLikeReport());
        var second = _generator.Generate(DemoLikeReport());

        Assert.Equal(first, second);
        Assert.DoesNotContain("\r", first);
        Assert.EndsWith("clients.\n", first);
    }

    // ---- ChangeReport ------------------------------------------------------------------------

    [Fact]
    public void ChangeReport_DerivesCountsScannedClientsAndUnaffectedOperations()
    {
        var report = DemoLikeReport();

        Assert.Equal((3, 1, 0, 2), (report.TotalChanges, report.BreakingChanges, report.WarningChanges, report.InfoChanges));
        Assert.Equal(["Android", "Frontend"], report.ScannedClients);
        Assert.Equal(["Android", "Frontend"], report.AffectedClients);
        Assert.Equal(3, report.AffectedOperations);
        Assert.Equal([new OperationSummary("Frontend", "CampaignCard", "frontend/ops.graphql")], report.UnaffectedOperations);
    }

    [Fact]
    public void ChangeReport_RequiresServiceName()
    {
        var analysis = new ImpactAnalyzer().Analyze([], []);

        Assert.Throws<ArgumentException>(() => ChangeReport.Create(" ", FixedTime, analysis, [], []));
    }

    // ---- Full pipeline on the demo ---------------------------------------------------------------

    [Fact]
    public void DemoPipeline_GeneratesReportWithAllKeyValues()
    {
        var markdown = _generator.Generate(TestReports.FromDemoFiles());

        var breaking = Section(markdown, "## 🔴 Breaking Changes");
        Assert.Contains("### `Campaign.videoUrl`", breaking);
        Assert.Contains("**Severity:** 🔴 Breaking", breaking);
        Assert.Contains("**Android**\n- `GetCampaign`\n- `GetCampaignDetails`", breaking);
        Assert.Contains("**Frontend**\n- `CampaignDetails`", breaking);

        var info = Section(markdown, "## 🟢 Informational Changes");
        Assert.Contains("### `Campaign.video`", info);
        Assert.Contains("### `CampaignVideo`", info);

        var clientImpact = Section(markdown, "## Client Impact");
        Assert.Contains("### 📱 Android", clientImpact);
        Assert.Contains("### 🌐 Frontend", clientImpact);

        Assert.Contains("**Frontend**\n- `CampaignCard`\n- `GetUser`", Section(markdown, "## ✅ Unaffected Operations"));
    }
}
