namespace Wallbang.Models;

public enum BracketMatchStatus { Pending, Live, Finished }

public class TournamentTeamEntry
{
    public string TeamId { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Seed { get; set; }
    public int AverageLevel { get; set; }
    public bool IsEliminated { get; set; }

    public string SeedLabel => Seed > 0 ? $"#{Seed}" : "—";
    public string InitialLetter => string.IsNullOrEmpty(Tag) ? "?" : Tag[..1].ToUpperInvariant();
}

public class BracketRound
{
    public string Id { get; set; } = string.Empty;
    public string TournamentId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public string Name { get; set; } = string.Empty;

    public Tournament? Tournament { get; set; }
    public List<BracketMatch> Matches { get; set; } = new();
}

public class BracketMatch
{
    public string Id { get; set; } = string.Empty;
    public string RoundId { get; set; } = string.Empty;
    public int Position { get; set; }
    public string TeamATag { get; set; } = "TBD";
    public string TeamBTag { get; set; } = "TBD";
    public int? ScoreA { get; set; }
    public int? ScoreB { get; set; }
    public BracketMatchStatus Status { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? MatchId { get; set; }

    public BracketRound? Round { get; set; }

    public string ScoreAText => ScoreA?.ToString() ?? "—";
    public string ScoreBText => ScoreB?.ToString() ?? "—";
    public bool HasScore => ScoreA.HasValue && ScoreB.HasValue;
    public bool AWon => HasScore && ScoreA > ScoreB;
    public bool BWon => HasScore && ScoreB > ScoreA;
    public bool IsLive => Status == BracketMatchStatus.Live;
    public bool IsFinished => Status == BracketMatchStatus.Finished;
    public string TimeLabel => ScheduledAt.HasValue
        ? ScheduledAt.Value.ToString("dd/MM HH:mm")
        : "A definir";
}
