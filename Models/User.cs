namespace Summit.Models;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string PrimaryRole { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public int Level { get; set; }
    public double WinRate { get; set; }
    public double KD { get; set; }
    public double HeadshotPercent { get; set; }
    public double AvgDamagePerRound { get; set; }
    public int TotalMatches { get; set; }
    public int TotalWins { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalAssists { get; set; }
    public int Elo { get; set; }
    public string FavoriteMap { get; set; } = string.Empty;
    public string FavoriteWeapon { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public string? TeamId { get; set; }
    public TeamRole TeamRole { get; set; } = TeamRole.Member;
    public DateTime? TeamJoinedAt { get; set; }
    public Team? Team { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    public List<UserBadge> Badges { get; set; } = new();

    // Flag manual (setado direto no banco) — sem fluxo de concessão pelo produto, ver
    // docs/spec/summit-fase-final/plan.md RF-08.
    public bool IsModerator { get; set; }

    public bool IsCaptain => TeamRole == TeamRole.Captain;
    public bool IsViceCaptain => TeamRole == TeamRole.ViceCaptain;
    // Só o capitão convida (espec-times §3.1/§7) — API já era captain-only, o client permitia
    // vice-líder também até essa correção (Fase 11, docs/spec/summit-fase-final/tasks.md).
    public bool CanInvite => TeamId != null && IsCaptain;
}
