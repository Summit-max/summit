namespace Summit.Models;

public enum NotificationType
{
    TeamInvite = 0,
    JoinRequestResolved = 1,
    RoleChanged = 2,
    OwnershipTransferred = 3,
    CheckInOpened = 4,
    LineupChanged = 5,
    TournamentFinished = 6,
    BadgeUnlocked = 7,
    ReportResolved = 8,
    MatchNoShow = 9
}

public class Notification
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RelatedId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
