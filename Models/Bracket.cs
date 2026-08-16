namespace Summit.Models;

// Aguardando → Vetos → Preparando servidor → Ao vivo → Finalizada (espec-campeonatos.md §7)
public enum BracketMatchStatus { Pending = 0, Live = 1, Finished = 2, Veto = 3, PreparingServer = 4 }

// Só relevante em eliminação dupla (espec-campeonatos.md §6) — eliminação simples usa só Upper.
public enum BracketSide { Upper = 0, Lower = 1, GrandFinal = 2 }

public class TournamentTeamEntry
{
    public string TeamId { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Seed { get; set; }
    public int AverageLevel { get; set; }
    public bool IsEliminated { get; set; }
    public CheckInStatus CheckIn { get; set; } = CheckInStatus.Waiting;

    public string SeedLabel => Seed > 0 ? $"#{Seed}" : "—";
    public string InitialLetter => string.IsNullOrEmpty(Tag) ? "?" : Tag[..1].ToUpperInvariant();
}

public class BracketRound
{
    public string Id { get; set; } = string.Empty;
    public string TournamentId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public BracketSide Side { get; set; } = BracketSide.Upper;

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

    // Avanço de chave (fase pós-partida) — pra onde o vencedor/perdedor desta partida vai.
    // LoserNext só é preenchido em partidas da Upper (eliminação dupla); na Lower, perder elimina.
    public string? NextMatchId { get; set; }
    public char? NextMatchSlot { get; set; }
    public string? LoserNextMatchId { get; set; }
    public char? LoserNextMatchSlot { get; set; }

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
