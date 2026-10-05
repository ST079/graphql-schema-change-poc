using Veel.GraphQL.SchemaIntelligence.Reporting;

namespace Veel.GraphQL.SchemaIntelligence.Notifications;

/// <summary>
/// Delivers a finished <see cref="ChangeReport"/> somewhere outside the tool.
/// Implementations only consume the report; they never take part in analysis.
/// </summary>
public interface INotificationSender
{
    /// <exception cref="NotificationException">Delivery failed or the sender is misconfigured.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task SendAsync(ChangeReport report, CancellationToken cancellationToken = default);
}
