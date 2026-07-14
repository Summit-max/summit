namespace Summit.Models;

public class Match
{
    public string Id { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public MatchStatus Status { get; set; } = MatchStatus.Finished;
    public int DurationMinutes { get; set; }

    public string TeamAId { get; set; } = string.Empty;
    public string TeamBId { get; set; } = string.Empty;
    public string TeamATag { get; set; } = string.Empty;
    public string TeamBTag { get; set; } = string.Empty;
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public int ScoreA { get; set; }
    public int ScoreB { get; set; }

    public string? TournamentId { get; set; }
    public string? TournamentName { get; set; }
    public string? BracketMatchId { get; set; }

    public List<MatchPlayer> Players { get; set; } = new();

    public string Score => $"{ScoreA}-{ScoreB}";
    public bool TeamAWon => ScoreA > ScoreB;
    public bool TeamBWon => ScoreB > ScoreA;
    public string WinnerTag => TeamAWon ? TeamATag : (TeamBWon ? TeamBTag : "—");
}

public class MatchPlayer
{
    public string Id { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TeamSide { get; set; } = "A"; // "A" or "B"

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int HeadshotKills { get; set; }
    public double AvgDamagePerRound { get; set; }
    public double Rating { get; set; }
    public bool IsMvp { get; set; }

    public Match? Match { get; set; }
    public User? User { get; set; }

    public double KD => Deaths == 0 ? Kills : Math.Round((double)Kills / Deaths, 2);
    public double HSPercent => Kills == 0 ? 0 : Math.Round((double)HeadshotKills / Kills, 2);
    public string KDText => KD.ToString("F2");
    public string HSText => $"{(int)(HSPercent * 100)}%";
    public string ADRText => AvgDamagePerRound.ToString("F1");
    public string RatingText => Rating.ToString("F2");
    public string KDAText => $"{Kills}-{Deaths}-{Assists}";
}
