using System.Net;
using System.Text.Json;
using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Notifications;
using Veel.GraphQL.SchemaIntelligence.Reporting;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

/// <summary>
/// The whole pipeline on the real demo files: the primary regression protection for the POC.
/// Loader → differ → classifier → operation analyzer → impact analyzer → report → Markdown / JSON / webhook.
/// </summary>
public class EndToEndTests
{
    private readonly MarkdownReportGenerator _markdown = new();
    private readonly JsonReportGenerator _json = new();

    [Fact]
    public void DemoScenario_DetectsClassifiesAndAttributesTheVideoUrlChange()
    {
        var run = TestReports.RunDemo();
        var report = run.Report;

        // What changed, and how severe each change is. videoUrl → video is two changes, not a rename.
        Assert.Equal(
            ["Breaking FieldRemoved Campaign.videoUrl", "Info FieldAdded Campaign.video", "Info TypeAdded CampaignVideo"],
            report.Changes.Select(c => $"{c.Severity} {c.Change.Change.ChangeType} {c.Change.Change.Path}"));
        Assert.Equal((3, 1, 0, 2), (report.TotalChanges, report.BreakingChanges, report.WarningChanges, report.InfoChanges));

        // Which clients and operations need attention: only those that actually select Campaign.videoUrl.
        Assert.Equal(["Android", "Frontend"], report.AffectedClients);
        Assert.Equal(3, report.AffectedOperations);
        Assert.Equal(
            ["Android: GetCampaign, GetCampaignDetails", "Frontend: CampaignDetails"],
            report.ClientImpacts.Select(i => $"{i.ClientName}: {string.Join(", ", i.AffectedOperationNames)}"));
        Assert.All(report.ClientImpacts, i => Assert.Equal(ChangeSeverity.Breaking, i.Severity));

        // Everything else is explicitly unaffected.
        Assert.Equal(
            ["Frontend/CampaignCard", "Frontend/GetUser"],
            report.UnaffectedOperations.Select(o => $"{o.ClientName}/{o.OperationName}"));

        // The replacement is offered only as a candidate.
        Assert.Equal(["Campaign.video"], report.Changes[0].PossibleMigrations);
        Assert.Equal("Update affected client operations to stop requesting Campaign.videoUrl.", report.Changes[0].RecommendedAction);
    }

    [Fact]
    public void DemoClients_KeepOperationOwnershipSeparate()
    {
        var run = TestReports.RunDemo();

        Assert.Equal(
            ["Android/GetCampaign", "Android/GetCampaignDetails", "Frontend/CampaignCard", "Frontend/CampaignDetails", "Frontend/GetUser"],
            run.Operations.Select(o => $"{o.ClientName}/{o.Name}"));
        Assert.All(run.Operations, o =>
            Assert.Contains($"demo/{o.ClientName.ToLowerInvariant()}/", o.FilePath.Replace('\\', '/')));
    }

    [Fact]
    public void DemoScenario_MarkdownAndJsonBothMatchTheChangeReport()
    {
        // ChangeReport is the source of truth; each output is checked against it independently.
        var report = TestReports.FromDemoFiles();
        var markdown = _markdown.Generate(report);
        var json = JsonDocument.Parse(_json.Generate(report)).RootElement;

        // Summary
        var summary = json.GetProperty("summary");
        var counts = new (string Markdown, string Json, int Expected)[]
        {
            ("Total Changes", "totalChanges", report.TotalChanges),
            ("🔴 Breaking", "breakingChanges", report.BreakingChanges),
            ("🟡 Warning", "warningChanges", report.WarningChanges),
            ("🟢 Informational", "infoChanges", report.InfoChanges),
            ("Affected Clients", "affectedClients", report.AffectedClients.Count),
            ("Affected Operations", "affectedOperations", report.AffectedOperations),
        };
        foreach (var (markdownLabel, jsonName, expected) in counts)
        {
            Assert.Contains($"| {markdownLabel} | {expected} |", markdown);
            Assert.Equal(expected, summary.GetProperty(jsonName).GetInt32());
        }

        // Changes, in report order
        var jsonChanges = json.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(report.Changes.Count, jsonChanges.Count);
        for (var i = 0; i < report.Changes.Count; i++)
        {
            var expected = report.Changes[i];
            Assert.Equal(expected.Change.Change.ChangeType.ToString(), jsonChanges[i].GetProperty("changeType").GetString());
            Assert.Equal(expected.Severity.ToString(), jsonChanges[i].GetProperty("severity").GetString());
            Assert.Equal(expected.Change.Change.TypeName, jsonChanges[i].GetProperty("schemaType").GetString());
            Assert.Equal(expected.Change.Change.FieldName, jsonChanges[i].GetProperty("fieldName").GetString());
            Assert.Contains($"### `{expected.Change.Change.Path}`\n\n**Change:**", markdown);
        }

        // Client impacts, in report order
        var jsonImpacts = json.GetProperty("clientImpacts").EnumerateArray().ToList();
        Assert.Equal(report.ClientImpacts.Count, jsonImpacts.Count);
        for (var i = 0; i < report.ClientImpacts.Count; i++)
        {
            var expected = report.ClientImpacts[i];
            Assert.Equal(expected.ClientName, jsonImpacts[i].GetProperty("clientName").GetString());
            Assert.Equal(expected.Severity.ToString(), jsonImpacts[i].GetProperty("severity").GetString());
            Assert.Equal(
                expected.AffectedOperationNames,
                jsonImpacts[i].GetProperty("affectedOperations").EnumerateArray().Select(o => o.GetString()!));
            foreach (var operation in expected.AffectedOperationNames)
            {
                Assert.Contains($"- `{operation}` — `{expected.Change.Change.Path}`", markdown);
            }
        }
    }

    [Fact]
    public void NoChangeScenario_ProducesEmptyButValidOutputs()
    {
        var run = TestReports.RunDemo(newSchemaFile: "demo/old-schema.graphql");
        var report = run.Report;

        Assert.Empty(run.Classified);
        Assert.Equal((0, 0, 0, 0), (report.TotalChanges, report.BreakingChanges, report.WarningChanges, report.InfoChanges));
        Assert.Empty(report.AffectedClients);
        Assert.Equal(0, report.AffectedOperations);
        Assert.Equal(5, report.UnaffectedOperations.Count);

        Assert.EndsWith("## Summary\n\nNo schema changes detected.\n", _markdown.Generate(report));

        var json = JsonDocument.Parse(_json.Generate(report)).RootElement;
        Assert.Equal(JsonValueKind.Array, json.GetProperty("changes").ValueKind);
        Assert.Equal(0, json.GetProperty("changes").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("clientImpacts").ValueKind);
        Assert.Equal(0, json.GetProperty("clientImpacts").GetArrayLength());
        Assert.Equal(0, json.GetProperty("summary").GetProperty("totalChanges").GetInt32());
    }

    [Fact]
    public async Task NoChangeScenario_WebhookSenderStillPostsTheSameDeterministicPayload()
    {
        // The sender itself does not filter; the CLI decides to skip empty reports (see CliTests).
        var report = TestReports.RunDemo(newSchemaFile: "demo/old-schema.graphql").Report;
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var sender = new WebhookNotificationSender(
            new HttpClient(handler), new WebhookOptions { Enabled = true, Url = "https://hooks.example.test/x" });

        await sender.SendAsync(report);
        await sender.SendAsync(report);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(_json.Generate(report), handler.Requests[0].Body);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
    }

    [Fact]
    public void IndependentRuns_ProduceIdenticalOutputs()
    {
        var first = TestReports.RunDemo();
        var second = TestReports.RunDemo();

        Assert.Equal(first.Classified, second.Classified);
        Assert.Equal(
            first.Operations.Select(o => (o.ClientName, o.Name, o.FilePath)),
            second.Operations.Select(o => (o.ClientName, o.Name, o.FilePath)));
        Assert.Equal(_markdown.Generate(first.Report), _markdown.Generate(second.Report));
        Assert.Equal(_json.Generate(first.Report), _json.Generate(second.Report));
    }
}
