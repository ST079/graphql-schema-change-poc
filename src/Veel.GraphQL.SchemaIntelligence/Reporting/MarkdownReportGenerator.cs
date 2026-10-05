using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Veel.GraphQL.SchemaIntelligence.Changes;
using Veel.GraphQL.SchemaIntelligence.Impact;

namespace Veel.GraphQL.SchemaIntelligence.Reporting;

/// <summary>
/// Formats a <see cref="ChangeReport"/> as GitHub-flavoured Markdown.
/// Formatting only: the same report always produces the same text (no clock, no I/O, <c>\n</c> line endings).
/// </summary>
public sealed class MarkdownReportGenerator
{
    private static readonly (ChangeSeverity Severity, string Heading)[] SeveritySections =
    [
        (ChangeSeverity.Breaking, "## 🔴 Breaking Changes"),
        (ChangeSeverity.Warning, "## 🟡 Warnings"),
        (ChangeSeverity.Info, "## 🟢 Informational Changes"),
    ];

    public string Generate(ChangeReport report)
    {
        var md = new MarkdownBuilder();

        md.Paragraph(report.BreakingChanges > 0 ? "# 🚨 GraphQL Schema Change Report" : "# GraphQL Schema Change Report");
        md.Paragraph($"**Service:** {Escape(report.ServiceName)}");
        md.Paragraph($"**Generated:** {report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC");
        if (report.OldSchemaSource is not null && report.NewSchemaSource is not null)
        {
            md.Paragraph($"**Compared:** {Code(report.OldSchemaSource)} → {Code(report.NewSchemaSource)}");
        }

        md.Paragraph("## Summary");
        if (report.TotalChanges == 0)
        {
            md.Paragraph("No schema changes detected.");
            return md.ToString();
        }

        WriteSummary(md, report);
        foreach (var (severity, heading) in SeveritySections)
        {
            WriteSeveritySection(md, report, severity, heading);
        }

        WriteClientImpact(md, report);
        WriteUnaffectedOperations(md, report);
        WriteChangesTable(md, report);
        WriteRecommendation(md, report);
        return md.ToString();
    }

    private static void WriteSummary(MarkdownBuilder md, ChangeReport report)
    {
        md.Lines(
            "| Metric | Count |",
            "|---|---:|",
            $"| Total Changes | {report.TotalChanges} |",
            $"| 🔴 Breaking | {report.BreakingChanges} |",
            $"| 🟡 Warning | {report.WarningChanges} |",
            $"| 🟢 Informational | {report.InfoChanges} |",
            $"| Affected Clients | {report.AffectedClients.Count} |",
            $"| Affected Operations | {report.AffectedOperations} |");
        md.Blank();
    }

    private static void WriteSeveritySection(MarkdownBuilder md, ChangeReport report, ChangeSeverity severity, string heading)
    {
        var impacts = report.Changes.Where(c => c.Severity == severity).ToList();
        if (impacts.Count == 0)
        {
            return;
        }

        md.Paragraph(heading);
        foreach (var impact in impacts)
        {
            WriteChange(md, report, impact);
        }
    }

    private static void WriteChange(MarkdownBuilder md, ChangeReport report, SchemaChangeImpact impact)
    {
        var change = impact.Change.Change;

        md.Paragraph($"### {Code(change.Path)}");
        md.Paragraph($"**Change:** {Humanize(change.ChangeType)}");
        if (change.OldType is not null)
        {
            md.Paragraph($"**Previous type:** {Code(change.OldType.ToString())}");
        }

        if (change.NewType is not null)
        {
            md.Paragraph($"**New type:** {Code(change.NewType.ToString())}");
        }

        if (change.ChangeType == ChangeType.TypeKindChanged)
        {
            md.Paragraph($"**Kind:** {Escape(change.OldValue ?? "?")} → {Escape(change.NewValue ?? "?")}");
        }

        if (change.ChangeType == ChangeType.DeprecationAdded && change.NewValue is not null)
        {
            md.Paragraph($"**Deprecation reason:** {Escape(change.NewValue)}");
        }

        md.Paragraph($"**Severity:** {SeverityLabel(impact.Severity)}");
        md.Paragraph($"**Reason:**  \n{Escape(impact.Change.Reason)}");

        // Info changes nobody uses need no client section; for anything riskier, say explicitly that no client was found.
        if (impact.HasClientImpact)
        {
            md.Paragraph("#### Affected Clients");
            foreach (var clientImpact in impact.ClientImpacts)
            {
                md.Line($"**{Escape(clientImpact.ClientName)}**");
                foreach (var operation in clientImpact.AffectedOperations)
                {
                    md.Line($"- {Code(operation.OperationName)}");
                }

                md.Blank();
            }
        }
        else if (impact.Severity != ChangeSeverity.Info)
        {
            md.Paragraph("#### Affected Clients");
            md.Paragraph(NoClientImpactMessage(report));
        }

        md.Paragraph("#### Recommended Action");
        md.Paragraph(FormatWithCode(impact.RecommendedAction, change.Path));

        if (impact.PossibleMigrations.Count > 0)
        {
            md.Line("> **Possible migration candidate:**  ");
            foreach (var candidate in impact.PossibleMigrations)
            {
                md.Line($"> {Code(change.Path)} → {Code(candidate)}  ");
            }

            md.Line(">");
            md.Line("> Developer verification required.");
            md.Blank();
        }
    }

    private static void WriteClientImpact(MarkdownBuilder md, ChangeReport report)
    {
        md.Paragraph("## Client Impact");
        if (report.ClientImpacts.Count == 0)
        {
            md.Paragraph(NoClientImpactMessage(report));
            return;
        }

        // Per client, each affected operation with every changed element it uses.
        var byClient = report.ClientImpacts
            .SelectMany(ci => ci.AffectedOperations.Select(op => (Client: ci.ClientName, Operation: op, Impact: ci)))
            .GroupBy(x => x.Client, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var client in byClient)
        {
            md.Paragraph($"### {ClientIcon(client.Key)} {Escape(client.Key)}");
            md.Paragraph("Affected operations:");

            var operations = client
                .GroupBy(x => (x.Operation.OperationName, x.Operation.FilePath))
                .OrderBy(g => g.Key.OperationName, StringComparer.Ordinal)
                .ThenBy(g => g.Key.FilePath, StringComparer.Ordinal);

            foreach (var operation in operations)
            {
                var uses = operation
                    .OrderByDescending(x => x.Impact.Severity)
                    .ThenBy(x => x.Impact.Change.Change.Path, StringComparer.Ordinal)
                    .Select(x => $"{Code(x.Impact.Change.Change.Path)} ({SeverityLabel(x.Impact.Severity)})");
                md.Line($"- {Code(operation.Key.OperationName)} — {string.Join(", ", uses)}");
            }

            md.Blank();
        }
    }

    private static void WriteUnaffectedOperations(MarkdownBuilder md, ChangeReport report)
    {
        if (report.UnaffectedOperations.Count == 0)
        {
            return;
        }

        md.Paragraph("## ✅ Unaffected Operations");
        foreach (var client in report.UnaffectedOperations.GroupBy(o => o.ClientName, StringComparer.Ordinal))
        {
            md.Line($"**{Escape(client.Key)}**");
            foreach (var operation in client)
            {
                md.Line($"- {Code(operation.OperationName)}");
            }

            md.Blank();
        }
    }

    private static void WriteChangesTable(MarkdownBuilder md, ChangeReport report)
    {
        md.Paragraph("## Changes");
        md.Lines(
            "| Severity | Change | Schema Element | Previous | New |",
            "|---|---|---|---|---|");

        foreach (var impact in report.Changes)
        {
            var change = impact.Change.Change;
            md.Line(
                $"| {SeverityLabel(impact.Severity)} | {Humanize(change.ChangeType)} | {TableCode(change.Path)} " +
                $"| {TableCode(change.OldType?.ToString())} | {TableCode(change.NewType?.ToString())} |");
        }

        md.Blank();
    }

    private static void WriteRecommendation(MarkdownBuilder md, ChangeReport report)
    {
        var clients = report.ClientImpacts
            .Where(ci => ci.Severity == ChangeSeverity.Breaking)
            .Select(ci => ci.ClientName)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(Escape)
            .ToList();

        if (clients.Count == 0)
        {
            return;
        }

        md.Paragraph("## Recommendation");
        md.Paragraph(
            $"The affected {JoinWithAnd(clients)} operations should be updated before the new schema is consumed by those clients.");
    }

    // ---- Formatting helpers ---------------------------------------------------------------------

    private static string NoClientImpactMessage(ChangeReport report) =>
        report.ScannedClients.Count == 0
            ? "No client operations were analyzed."
            : "No currently detected client operations are affected.";

    private static string SeverityLabel(ChangeSeverity severity) => severity switch
    {
        ChangeSeverity.Breaking => "🔴 Breaking",
        ChangeSeverity.Warning => "🟡 Warning",
        _ => "🟢 Info",
    };

    private static string ClientIcon(string clientName) => clientName.ToLowerInvariant() switch
    {
        "android" or "ios" => "📱",
        "frontend" or "web" => "🌐",
        _ => "🧩",
    };

    /// <summary><c>FieldRemoved</c> → <c>Field removed</c>.</summary>
    private static string Humanize(ChangeType changeType)
    {
        var name = changeType.ToString();
        var result = new StringBuilder().Append(name[0]);
        foreach (var c in name.AsSpan(1))
        {
            result.Append(char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : c.ToString());
        }

        return result.ToString();
    }

    private static string JoinWithAnd(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    /// <summary>Escapes characters that would otherwise be read as Markdown formatting.</summary>
    private static string Escape(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '|' or '<' or '>')
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>An inline code span; a longer fence is used when the value itself contains a backtick.</summary>
    private static string Code(string value) =>
        value.Contains('`') ? $"`` {value} ``" : $"`{value}`";

    /// <summary>A code span safe inside a table cell (pipes must be escaped even in code), or an em dash when empty.</summary>
    private static string TableCode(string? value) =>
        value is null ? "—" : Code(value).Replace("|", "\\|");

    /// <summary>
    /// Escapes free text and renders whole occurrences of the schema path as code.
    /// Only whole names match: <c>Query.title</c> does not match inside <c>Query.titleText</c>.
    /// </summary>
    private static string FormatWithCode(string text, string path)
    {
        // Name characters, plus '\' because escaping turns '_' into '\_'.
        const string NameCharacter = @"[A-Za-z0-9_\\]";
        var pattern = $@"(?<!{NameCharacter}|\.){Regex.Escape(Escape(path))}(?!{NameCharacter})";
        return Regex.Replace(Escape(text), pattern, _ => Code(path));
    }

    /// <summary>Builds Markdown with <c>\n</c> line endings and blank-line-separated blocks.</summary>
    private sealed class MarkdownBuilder
    {
        private readonly StringBuilder _builder = new();

        public void Line(string line) => _builder.Append(line).Append('\n');

        public void Lines(params string[] lines)
        {
            foreach (var line in lines)
            {
                Line(line);
            }
        }

        public void Blank() => _builder.Append('\n');

        /// <summary>A block followed by a blank line.</summary>
        public void Paragraph(string text)
        {
            Line(text);
            Blank();
        }

        /// <summary>The document, ending in exactly one newline.</summary>
        public override string ToString() => _builder.ToString().TrimEnd('\n') + "\n";
    }
}
