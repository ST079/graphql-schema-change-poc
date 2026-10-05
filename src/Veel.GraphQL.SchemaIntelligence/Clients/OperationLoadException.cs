namespace Veel.GraphQL.SchemaIntelligence.Clients;

/// <summary>
/// Raised when client operations cannot be loaded: missing directory, invalid GraphQL,
/// or an unresolvable fragment. The message is intended to be shown directly to CLI users.
/// </summary>
public sealed class OperationLoadException(string message, string? filePath = null, int? line = null, int? column = null)
    : Exception(message)
{
    /// <summary>The document that failed, when the problem is file-specific.</summary>
    public string? FilePath { get; } = filePath;

    /// <summary>Position of a syntax error, when known.</summary>
    public int? Line { get; } = line;
    public int? Column { get; } = column;
}
