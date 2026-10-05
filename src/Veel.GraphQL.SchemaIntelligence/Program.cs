using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Clients;
using Veel.GraphQL.SchemaIntelligence.Impact;
using Veel.GraphQL.SchemaIntelligence.Notifications;
using Veel.GraphQL.SchemaIntelligence.Reporting;
using Veel.GraphQL.SchemaIntelligence.Schema;

// Minimal CLI: wires the pipeline together. Analysis, reporting and notification live in their own namespaces.
const string Usage =
    "Usage: analyze --old-schema <path> --new-schema <path> [--android <dir>] [--frontend <dir>] " +
    "[--service <name>] [--output <dir>] [--format markdown|json|all] [--config <file>] [--webhook <url>]";
const string ReportFileName = "schema-change-report";

// Client option → client name. Every client is analyzed identically.
var clientOptions = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["--android"] = "Android",
    ["--frontend"] = "Frontend",
};

Console.WriteLine("GraphQL Schema Intelligence");
Console.WriteLine("===========================");
Console.WriteLine();

if (args.Length == 0 || args[0] != "analyze")
{
    Console.Error.WriteLine(Usage);
    return 1;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
{
    var known = args[i] is "--old-schema" or "--new-schema" or "--service" or "--output" or "--format" or "--config" or "--webhook" || clientOptions.ContainsKey(args[i]);
    if (!known || i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"ERROR: Unknown or incomplete option '{args[i]}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    options[args[i]] = args[i + 1];
}

if (!options.TryGetValue("--old-schema", out var oldSchemaPath) || !options.TryGetValue("--new-schema", out var newSchemaPath))
{
    Console.Error.WriteLine("ERROR: Both --old-schema and --new-schema are required.");
    Console.Error.WriteLine(Usage);
    return 1;
}

var serviceName = options.GetValueOrDefault("--service", "GraphQL API");
var outputDirectory = options.GetValueOrDefault("--output", "reports");
var format = options.GetValueOrDefault("--format", "all");
if (format is not ("markdown" or "json" or "all"))
{
    Console.Error.WriteLine($"ERROR: Unknown format '{format}'. Use markdown, json or all.");
    return 1;
}

// Webhook settings: config file, then environment variables, then --webhook (which also enables it).
WebhookOptions webhookOptions;
try
{
    webhookOptions = WebhookOptions.Load(options.GetValueOrDefault("--config"), Environment.GetEnvironmentVariable);
}
catch (NotificationException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

if (options.TryGetValue("--webhook", out var webhookUrl))
{
    webhookOptions.Enabled = true;
    webhookOptions.Url = webhookUrl;
}

var clients = clientOptions
    .Where(o => options.ContainsKey(o.Key))
    .Select(o => new ClientDefinition(o.Value, options[o.Key]))
    .ToList();

GraphQLSchema oldSchema, newSchema;
try
{
    Console.WriteLine("Loading old schema...");
    oldSchema = SchemaLoader.LoadFromFile(oldSchemaPath);
    Console.WriteLine("Loading new schema...");
    newSchema = SchemaLoader.LoadFromFile(newSchemaPath);
}
catch (SchemaLoadException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

Console.WriteLine();
Console.WriteLine("Comparing schemas...");
Console.WriteLine();

var classified = new ChangeClassifier().Classify(new SchemaDiffer().Compare(oldSchema, newSchema));
PrintChanges(classified);

var operationAnalyzer = new OperationAnalyzer();
var operations = new List<GraphQLOperation>();
try
{
    foreach (var client in clients)
    {
        Console.WriteLine($"Analyzing {client.Name} operations...");
        operations.AddRange(operationAnalyzer.LoadOperations(client));
    }
}
catch (OperationLoadException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

// Client operations are written against the schema they use today: the old one.
var references = classified.SelectMany(c => operationAnalyzer.FindAffectedOperations(c, operations, oldSchema));
var analysis = new ImpactAnalyzer().Analyze(classified, references);
if (classified.Count > 0 && clients.Count > 0)
{
    PrintImpact(analysis, clients);
}

var report = ChangeReport.Create(
    serviceName, DateTimeOffset.UtcNow, analysis, clients, operations, oldSchemaPath, newSchemaPath);

// Every generator formats the same ChangeReport; writing files is the CLI's job, not theirs.
var outputs = new List<(string Path, Func<ChangeReport, string> Generate)>();
if (format is "markdown" or "all")
{
    outputs.Add((Path.Combine(outputDirectory, $"{ReportFileName}.md"), new MarkdownReportGenerator().Generate));
}

if (format is "json" or "all")
{
    outputs.Add((Path.Combine(outputDirectory, $"{ReportFileName}.json"), new JsonReportGenerator().Generate));
}

foreach (var (path, generate) in outputs)
{
    try
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(path, generate(report));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"ERROR: Could not write report to {path}: {ex.Message}");
        return 1;
    }
}

Console.WriteLine();
Console.WriteLine("Reports generated:");
Console.WriteLine();
outputs.ForEach(o => Console.WriteLine(o.Path));

// In GitHub Actions, surface breaking changes as a run annotation. Informational only: the exit code stays 0.
if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && report.BreakingChanges > 0)
{
    Console.WriteLine(
        $"::warning title=GraphQL schema change::{report.BreakingChanges} breaking schema change(s) detected. " +
        "This does not fail the build; review the GraphQL Schema Change Report in the job summary.");
}

// Notification is optional and only meaningful when something changed. Reports are already written either way.
if (webhookOptions.Enabled && report.TotalChanges > 0)
{
    // The sender enforces WebhookOptions.TimeoutSeconds itself, so the client's own 100 s default must not cut in first.
    using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    INotificationSender notifier = new WebhookNotificationSender(httpClient, webhookOptions);
    Console.WriteLine();
    Console.WriteLine("Sending webhook notification...");
    try
    {
        await notifier.SendAsync(report);
    }
    catch (NotificationException ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        return 1;
    }

    Console.WriteLine("Webhook notification sent.");
}
else if (webhookOptions.Enabled)
{
    Console.WriteLine();
    Console.WriteLine("Webhook notification skipped: no schema changes.");
}

return 0;

static void PrintChanges(IReadOnlyList<ClassifiedSchemaChange> changes)
{
    if (changes.Count == 0)
    {
        Console.WriteLine("No schema changes detected.");
        return;
    }

    Console.WriteLine("GraphQL Schema Changes");
    Console.WriteLine("======================");

    foreach (var (change, severity, _) in changes)
    {
        Console.WriteLine();
        Console.WriteLine(change.ChangeType);
        Console.WriteLine($"  Severity: {severity}");
        Console.WriteLine($"  Type: {change.TypeName}");
        WriteIfPresent("Field", change.FieldName);
        WriteIfPresent("Argument", change.ArgumentName);
        WriteIfPresent("Old Type", change.OldType?.ToString());
        WriteIfPresent("New Type", change.NewType?.ToString());
        WriteIfPresent("Old Value", change.OldValue);
        WriteIfPresent("New Value", change.NewValue);
    }

    Console.WriteLine();
    Console.WriteLine($"{changes.Count} change(s) detected.");
    Console.WriteLine($"  Breaking: {changes.Count(c => c.Severity == ChangeSeverity.Breaking)}");
    Console.WriteLine($"  Warning:  {changes.Count(c => c.Severity == ChangeSeverity.Warning)}");
    Console.WriteLine($"  Info:     {changes.Count(c => c.Severity == ChangeSeverity.Info)}");
    Console.WriteLine();
}

static void PrintImpact(ImpactAnalysis analysis, IReadOnlyList<ClientDefinition> clients)
{
    Console.WriteLine();
    Console.WriteLine("Client Impact");
    Console.WriteLine("=============");

    foreach (var impact in analysis.Changes)
    {
        Console.WriteLine();
        Console.WriteLine($"{impact.Severity.ToString().ToUpperInvariant()}  {impact.Change.Change.ChangeType}  {impact.Change.Change.Path}");
        if (!impact.HasClientImpact)
        {
            Console.WriteLine("  No affected client operations detected.");
        }

        foreach (var clientImpact in impact.ClientImpacts)
        {
            Console.WriteLine($"  {clientImpact.ClientName}:");
            foreach (var operation in clientImpact.AffectedOperations)
            {
                Console.WriteLine($"    - {operation.OperationName}  ({string.Join(", ", operation.Usages)})");
            }
        }

        Console.WriteLine($"  Action: {impact.RecommendedAction}");
        foreach (var candidate in impact.PossibleMigrations)
        {
            Console.WriteLine($"  Possible migration candidate (developer verification required): {impact.Change.Change.Path} → {candidate}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Breaking changes: {analysis.Summary.BreakingChanges}");
    foreach (var client in clients)
    {
        var count = analysis.Summary.Clients.FirstOrDefault(c => c.ClientName == client.Name)?.AffectedOperations ?? 0;
        Console.WriteLine($"Affected {client.Name} operations: {count}");
    }
}

static void WriteIfPresent(string label, string? value)
{
    if (value is not null)
    {
        Console.WriteLine($"  {label}: {value}");
    }
}
