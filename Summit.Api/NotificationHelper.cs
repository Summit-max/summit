using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Helper de notificação in-app (docs/spec/summit-fase-final/plan.md RF-06) — mesmo padrão do
/// `Audit`: só adiciona ao DbContext, não salva sozinho (entra na mesma transação da ação que
/// a originou).
/// </summary>
public static class NotificationHelper
{
    public static Task Notify(ApiDbContext db, string userId, NotificationType type, string message, string? relatedId = null)
    {
        db.Notifications.Add(new Notification
        {
            Id = $"ntf_{Guid.NewGuid():N}",
            UserId = userId,
            Type = type,
            Message = message,
            RelatedId = relatedId
        });
        return Task.CompletedTask;
    }
}
