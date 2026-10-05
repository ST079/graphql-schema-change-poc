using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Veel.GraphQL.SchemaIntelligence.Notifications;
using Veel.GraphQL.SchemaIntelligence.Schema;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

/// <summary>
/// Runs the compiled CLI as a separate process, exactly as CI will: exit codes, console output and report files.
/// </summary>
public sealed class CliTests : IDisposable
{
    private static readonly string[] WebhookVariables =
    [
        WebhookOptions.EnabledVariable, WebhookOptions.UrlVariable, WebhookOptions.LegacyUrlVariable,
        WebhookOptions.TimeoutSecondsVariable, WebhookOptions.BearerTokenVariable,
        "GITHUB_ACTIONS",
    ];

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("schema-intelligence-cli-");

    public void Dispose() => _temp.Delete(recursive: true);

    private string OutputDirectory => Path.Combine(_temp.FullName, "reports");

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + StandardError;
    }

    private static async Task<CliResult> RunCli(IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = TestPaths.FromRepositoryRoot("."),
        };
        startInfo.ArgumentList.Add(typeof(SchemaLoader).Assembly.Location);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Isolate from the developer's or CI runner's own settings (GitHub sets GITHUB_ACTIONS on its runners).
        foreach (var variable in WebhookVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return new CliResult(process.ExitCode, await stdout, await stderr);
    }

    private Task<CliResult> RunCli(params string[] args) => RunCli(null, args);

    private string[] DemoArgs(params string[] extra) =>
    [
        "analyze",
        "--old-schema", "demo/old-schema.graphql",
        "--new-schema", "demo/new-schema.graphql",
        "--android", "demo/android",
        "--frontend", "demo/frontend",
        "--service", "Cerberus",
        "--output", OutputDirectory,
        .. extra,
    ];

    /// <summary>A localhost port with nothing listening on it, so connections are refused immediately.</summary>
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void AssertNoStackTrace(CliResult result) =>
        Assert.DoesNotContain("   at ", result.AllOutput);

    // ---- Successful runs ----------------------------------------------------------------------

    [Fact]
    public async Task Demo_PrintsSummaryWritesBothReportsAndExitsZero()
    {
        var result = await RunCli(DemoArgs());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Breaking changes: 1", result.StandardOutput);
        Assert.Contains("Affected Android operations: 2", result.StandardOutput);
        Assert.Contains("Affected Frontend operations: 1", result.StandardOutput);
        Assert.Equal("", result.StandardError);

        var markdown = await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.md"));
        Assert.StartsWith("# 🚨 GraphQL Schema Change Report\n\n**Service:** Cerberus\n", markdown);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.json")));
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("breakingChanges").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("summary").GetProperty("affectedOperations").GetInt32());
    }

    [Fact]
    public async Task IdenticalSchemas_ReportNoChanges()
    {
        var result = await RunCli(
            "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/old-schema.graphql",
            "--android", "demo/android", "--output", OutputDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No schema changes detected.", result.StandardOutput);
        Assert.EndsWith("No schema changes detected.\n", await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.md")));
    }

    [Theory]
    [InlineData("markdown", "schema-change-report.md")]
    [InlineData("json", "schema-change-report.json")]
    public async Task Format_WritesOnlyTheRequestedReport(string format, string expectedFile)
    {
        var result = await RunCli(DemoArgs("--format", format));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([expectedFile], Directory.GetFiles(OutputDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task RepeatedRuns_ProduceIdenticalReportsApartFromTheTimestamp()
    {
        static string WithoutTimestamp(string text) =>
            string.Join('\n', text.Split('\n').Where(l => !l.Contains("Generated") && !l.Contains("generatedAt")));

        await RunCli(DemoArgs());
        var firstMarkdown = await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.md"));
        var firstJson = await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.json"));

        await RunCli(DemoArgs());

        Assert.Equal(WithoutTimestamp(firstMarkdown), WithoutTimestamp(await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.md"))));
        Assert.Equal(WithoutTimestamp(firstJson), WithoutTimestamp(await File.ReadAllTextAsync(Path.Combine(OutputDirectory, "schema-change-report.json"))));
    }

    // ---- CI behaviour ---------------------------------------------------------------------------

    [Fact]
    public async Task BreakingChanges_DoNotFailTheRun()
    {
        // Policy: analysis is informational. Breaking changes are reported, never turned into a failing exit code.
        var result = await RunCli(DemoArgs());

        Assert.Contains("Breaking changes: 1", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task InGitHubActions_BreakingChangesAreAnnotatedAsWarning()
    {
        var result = await RunCli(new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true" }, DemoArgs());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("::warning title=GraphQL schema change::1 breaking schema change(s) detected.", result.StandardOutput);
    }

    [Fact]
    public async Task InGitHubActions_NoBreakingChanges_NoAnnotation()
    {
        var result = await RunCli(
            new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true" },
            "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/old-schema.graphql", "--output", OutputDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("::warning", result.StandardOutput);
    }

    [Fact]
    public async Task OutsideGitHubActions_NoAnnotation()
    {
        var result = await RunCli(DemoArgs());

        Assert.DoesNotContain("::warning", result.StandardOutput);
    }

    // ---- Failures: clear message, exit code 1, no stack trace -------------------------------------

    [Theory]
    [InlineData("missing old schema", "ERROR: Schema file does not exist: missing.graphql",
        "analyze", "--old-schema", "missing.graphql", "--new-schema", "demo/new-schema.graphql")]
    [InlineData("missing client directory", "ERROR: Android GraphQL directory does not exist: demo/ios",
        "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/new-schema.graphql", "--android", "demo/ios")]
    [InlineData("unknown option", "ERROR: Unknown or incomplete option '--bogus'.",
        "analyze", "--old-schema", "demo/old-schema.graphql", "--bogus", "x")]
    [InlineData("missing required option", "ERROR: Both --old-schema and --new-schema are required.",
        "analyze", "--old-schema", "demo/old-schema.graphql")]
    [InlineData("unknown format", "ERROR: Unknown format 'xml'. Use markdown, json or all.",
        "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/new-schema.graphql", "--format", "xml")]
    [InlineData("missing config file", "ERROR: Configuration file does not exist: missing.json",
        "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/new-schema.graphql", "--config", "missing.json")]
    [InlineData("no command", "Usage: analyze")]
    public async Task InvalidInput_FailsWithClearError(string scenario, string expectedError, params string[] args)
    {
        var result = await RunCli(args);

        Assert.True(result.ExitCode == 1, $"{scenario}: expected exit code 1, got {result.ExitCode}");
        Assert.Contains(expectedError, result.StandardError);
        AssertNoStackTrace(result);
    }

    [Fact]
    public async Task InvalidSchema_FailsWithLineAndColumn()
    {
        var schema = Path.Combine(_temp.FullName, "broken.graphql");
        await File.WriteAllTextAsync(schema, "type Query {\n  campaign: \n}");

        var result = await RunCli("analyze", "--old-schema", schema, "--new-schema", "demo/new-schema.graphql");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: Schema is invalid GraphQL", result.StandardError);
        Assert.Contains("Line: 3", result.StandardError);
        AssertNoStackTrace(result);
    }

    [Fact]
    public async Task InvalidOperation_FailsWithFileLineAndColumn()
    {
        var clientDirectory = Directory.CreateDirectory(Path.Combine(_temp.FullName, "android"));
        await File.WriteAllTextAsync(Path.Combine(clientDirectory.FullName, "Broken.graphql"), "query Broken {\n  campaign {\n    id(\n  }\n}");

        var result = await RunCli(
            "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/new-schema.graphql",
            "--android", clientDirectory.FullName, "--output", OutputDirectory);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: Failed to parse operation:", result.StandardError);
        Assert.Contains("Broken.graphql", result.StandardError);
        Assert.Contains("Line:", result.StandardError);
        AssertNoStackTrace(result);
    }

    // ---- Webhook ------------------------------------------------------------------------------

    [Fact]
    public async Task WebhookEnabledWithoutUrl_FailsClearly()
    {
        var result = await RunCli(new Dictionary<string, string> { [WebhookOptions.EnabledVariable] = "true" }, DemoArgs());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: Webhook notification is enabled but no webhook URL is configured.", result.StandardError);
        Assert.True(File.Exists(Path.Combine(OutputDirectory, "schema-change-report.json")), "Reports are written before notifying");
    }

    [Fact]
    public async Task UnreachableWebhook_FailsWithoutExposingUrlOrToken()
    {
        var url = $"http://127.0.0.1:{ClosedPort()}/hook/secret-path-token";
        const string token = "cli-test-bearer-token";

        var result = await RunCli(new Dictionary<string, string> { [WebhookOptions.BearerTokenVariable] = token }, DemoArgs("--webhook", url));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERROR: Webhook notification failed: the webhook endpoint could not be reached.", result.StandardError);
        Assert.DoesNotContain("secret-path-token", result.AllOutput);
        Assert.DoesNotContain(token, result.AllOutput);
        foreach (var file in Directory.GetFiles(OutputDirectory))
        {
            Assert.DoesNotContain(token, await File.ReadAllTextAsync(file));
        }

        AssertNoStackTrace(result);
    }

    [Fact]
    public async Task NoChanges_SkipsWebhook()
    {
        // The endpoint is unreachable, so the run would fail if the CLI tried to send.
        var result = await RunCli(
            "analyze", "--old-schema", "demo/old-schema.graphql", "--new-schema", "demo/old-schema.graphql",
            "--output", OutputDirectory, "--webhook", $"http://127.0.0.1:{ClosedPort()}/hook");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Webhook notification skipped: no schema changes.", result.StandardOutput);
    }
}
