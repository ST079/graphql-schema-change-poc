namespace Veel.GraphQL.SchemaIntelligence.Schema;

/// <summary>
/// Raised when a schema cannot be loaded: missing file, invalid GraphQL syntax, or failed validation.
/// The message is intended to be shown directly to CLI users.
/// </summary>
public sealed class SchemaLoadException : Exception
{
    public SchemaLoadException(string source, string message, IReadOnlyList<string>? errors = null, int? line = null, int? column = null)
        : base(message)
    {
        SchemaSource = source;
        Errors = errors ?? [message];
        Line = line;
        Column = column;
    }

    /// <summary>File path or source name of the schema that failed to load.</summary>
    public string SchemaSource { get; }

    /// <summary>Individual problems found. Validation can report several at once.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Position of a syntax error, when known.</summary>
    public int? Line { get; }
    public int? Column { get; }
}
