namespace Veel.GraphQL.SchemaIntelligence.Notifications;

/// <summary>
/// A notification could not be delivered, or its configuration is invalid.
/// Messages are safe to show to users: they never contain URLs, tokens or response bodies.
/// </summary>
public sealed class NotificationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
