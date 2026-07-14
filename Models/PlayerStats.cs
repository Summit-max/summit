namespace Summit.Models;

// Aggregated stats DTO computed from User + recent matches (not a DB entity).
public class PlayerStats
{
    public string UserId { get; set; } = string.Empty;
    public double KD { get; set; }
    public double HeadshotPercent { get; set; }
    public double WinRate { get; set; }
    public int TotalMatches { get; set; }
    public int TotalWins { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalAssists { get; set; }
    public double AvgDamagePerRound { get; set; }
    public string FavoriteMap { get; set; } = string.Empty;
    public string FavoriteWeapon { get; set; } = string.Empty;
    public List<RecentPerformance> RecentPerformance { get; set; } = new();
}

public class RecentPerformance
{
    public DateTime Date { get; set; }
    public string Map { get; set; } = string.Empty;
    public bool Won { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public string Score { get; set; } = string.Empty;

    public double KD => Deaths == 0 ? Kills : Math.Round((double)Kills / Deaths, 2);
}
