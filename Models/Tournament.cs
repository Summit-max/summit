namespace Wallbang.Models;

public enum TournamentStatus { Open, InProgress, Finished, Upcoming }

public class Tournament
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Game { get; set; } = "CS2";
    public string Format { get; set; } = string.Empty;
    public string BannerUrl { get; set; } = string.Empty;
    public TournamentStatus Status { get; set; }
    public string Prize { get; set; } = string.Empty;
    public int MaxTeams { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Rules { get; set; } = string.Empty;
    public string Organizer { get; set; } = string.Empty;
    public string MapPoolCsv { get; set; } = string.Empty;

    public List<TournamentTeam> TournamentTeams { get; set; } = new();
    public List<BracketRound> Bracket { get; set; } = new();

    public List<string> MapPool => string.IsNullOrWhiteSpace(MapPoolCsv)
        ? new List<string>()
        : MapPoolCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public int RegisteredTeams => TournamentTeams?.Count ?? 0;
    public List<TournamentTeamEntry> Teams => TournamentTeams?
        .OrderBy(tt => tt.Seed)
        .Select(tt => new TournamentTeamEntry
        {
            TeamId = tt.TeamId,
            Tag = tt.Team?.Tag ?? "???",
            Name = tt.Team?.Name ?? "—",
            Seed = tt.Seed,
            AverageLevel = tt.Team?.AverageLevel ?? 0,
            IsEliminated = tt.IsEliminated
        }).ToList() ?? new();

    public bool IsRegistered { get; set; } // set by service when loading for current user

    public string MapPoolText => MapPool.Count > 0 ? string.Join(" • ", MapPool) : "A definir";
    public string TeamsCountText => $"{RegisteredTeams}/{MaxTeams}";
    public int SlotsRemaining => MaxTeams - RegisteredTeams;
    public double SlotsFillPercent => MaxTeams > 0 ? (double)RegisteredTeams / MaxTeams : 0;

    public string StatusLabel => Status switch
    {
        TournamentStatus.Open       => "INSCRIÇÕES ABERTAS",
        TournamentStatus.InProgress => "EM ANDAMENTO",
        TournamentStatus.Finished   => "ENCERRADO",
        TournamentStatus.Upcoming   => "EM BREVE",
        _                           => ""
    };

    public string CountdownLabel
    {
        get
        {
            if (Status == TournamentStatus.Finished) return "ENCERRADO";
            if (Status == TournamentStatus.InProgress) return "AO VIVO AGORA";
            var days = (int)(StartDate.Date - DateTime.Now.Date).TotalDays;
            return days switch
            {
                0            => "COMEÇA HOJE",
                1            => "COMEÇA AMANHÃ",
                > 1          => $"EM {days} DIAS",
                _            => $"HÁ {-days} DIAS"
            };
        }
    }
}

public class TournamentTeam
{
    public string Id { get; set; } = string.Empty;
    public string TournamentId { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public int Seed { get; set; }
    public bool IsEliminated { get; set; }
    public int? FinalPosition { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public Tournament? Tournament { get; set; }
    public Team? Team { get; set; }
}
