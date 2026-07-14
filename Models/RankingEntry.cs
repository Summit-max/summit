namespace Summit.Models;

public class RankingPlayer
{
    public int Position { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string TeamTag { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public int Elo { get; set; }
    public int Level { get; set; }
    public double WinRate { get; set; }
    public double KD { get; set; }
    public int Matches { get; set; }

    public string InitialLetter =>
        string.IsNullOrEmpty(Nickname) ? "?" : Nickname[..1].ToUpperInvariant();
    public bool   HasAvatar   => !string.IsNullOrEmpty(AvatarUrl);
    public string WinRateText => $"{WinRate:P0}";
    public string KDText      => $"{KD:F2}";
    public string EloText     => Elo.ToString("N0");
}

public class RankingTeam
{
    public int Position { get; set; }
    public string TeamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public int Elo { get; set; }
    public double WinRate { get; set; }
    public int TournamentsWon { get; set; }
    public int Matches { get; set; }

    public string InitialLetter => string.IsNullOrEmpty(Tag) ? "?" : Tag[..1].ToUpperInvariant();
    public string WinRateText   => $"{WinRate:P0}";
    public string EloText       => Elo.ToString("N0");
}
