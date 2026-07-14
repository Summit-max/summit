namespace Summit.Models;

public class Team
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CaptainId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Elo { get; set; }
    public int TournamentsPlayed { get; set; }
    public int TournamentsWon { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }

    public List<User> Members { get; set; } = new();
    public List<TeamInvitation> Invitations { get; set; } = new();

    public double WinRate => MatchesPlayed > 0
        ? Math.Round((double)MatchesWon / MatchesPlayed, 2)
        : 0;

    public int AverageLevel => Members.Count == 0 ? 0 : (int)Members.Average(m => m.Level);
    public string InitialLetter => string.IsNullOrEmpty(Tag) ? "?" : Tag[..1].ToUpperInvariant();
}

public class TeamInvitation
{
    public string Id { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string InvitedUserId { get; set; } = string.Empty;
    public string InvitedById { get; set; } = string.Empty;
    public TeamInvitationStatus Status { get; set; } = TeamInvitationStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    public Team? Team { get; set; }
    public User? InvitedUser { get; set; }
    public User? InvitedBy { get; set; }
}
