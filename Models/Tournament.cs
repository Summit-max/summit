namespace Summit.Models;

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

    // ───── Configuração do campeonato (espec-campeonatos.md §1) ─────
    public string Region { get; set; } = "América do Sul";
    public int MinTeams { get; set; } = 2;
    public bool IsPaidEntry { get; set; }
    public string EntryFee { get; set; } = string.Empty;
    public TournamentFormat FormatType { get; set; } = TournamentFormat.SingleElimination;
    public SeriesFormat Series { get; set; } = SeriesFormat.MD1;
    public SeriesFormat FinalSeries { get; set; } = SeriesFormat.MD1;
    public bool BracketReset { get; set; }                 // grande final da eliminação dupla
    public int NoShowMinutes { get; set; } = 10;
    public int PausesPerTeam { get; set; } = 4;
    public string OvertimeConfig { get; set; } = "MR3 $10.000";

    // ───── Fase pós-partida: organizador real + config do formato suíço ─────
    public string OrganizerUserId { get; set; } = string.Empty;
    public int SwissTargetWins { get; set; } = 3;
    public int SwissEliminationLosses { get; set; } = 2;

    // Janelas automáticas (§3-4): inscrições fecham T-12h; check-in T-1h → T-30min
    public DateTime RegistrationClosesAt => StartDate.AddHours(-12);
    public DateTime CheckInOpensAt => StartDate.AddHours(-1);
    public DateTime CheckInClosesAt => StartDate.AddMinutes(-30);

    public bool IsRegistrationOpen =>
        Status == TournamentStatus.Open && DateTime.UtcNow < RegistrationClosesAt;
    public bool IsCheckInOpen =>
        DateTime.UtcNow >= CheckInOpensAt && DateTime.UtcNow < CheckInClosesAt
        && Status != TournamentStatus.Finished;
    public bool CanEditLineup => IsRegistered && DateTime.UtcNow < CheckInOpensAt;

    public bool IsOrganizer { get; set; } // setado pelo service ao carregar, comparando com o usuário atual
    public bool CanEdit => IsOrganizer && DateTime.UtcNow < RegistrationClosesAt;

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
            IsEliminated = tt.IsEliminated,
            CheckIn = tt.CheckIn
        }).ToList() ?? new();

    public bool IsRegistered { get; set; } // set by service when loading for current user
    public string? MyTeamId { get; set; }  // set by service when loading for current user
    public bool HasCheckedIn => Teams.FirstOrDefault(t => t.TeamId == MyTeamId)?.CheckIn == CheckInStatus.Confirmed;
    public bool CanCheckIn => IsRegistered && IsCheckInOpen && !HasCheckedIn;

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
            var span = StartDate - DateTime.UtcNow;
            if (span.TotalSeconds <= 0) return "COMEÇANDO...";
            if (span.TotalDays >= 2) return $"EM {(int)span.TotalDays} DIAS";
            if (span.TotalHours >= 1) return $"EM {(int)span.TotalHours}H {span.Minutes:D2}M";
            return $"EM {span.Minutes}M {span.Seconds:D2}S";
        }
    }

    public string RegistrationLabel => IsRegistrationOpen
        ? "INSCREVER TIME"
        : "INSCRIÇÕES ENCERRADAS";
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

    // Escalação + check-in (espec-times.md §16-20, espec-campeonatos.md §4)
    public string? CaptainUserId { get; set; }
    public CheckInStatus CheckIn { get; set; } = CheckInStatus.Waiting;
    public DateTime? CheckedInAt { get; set; }

    public Tournament? Tournament { get; set; }
    public Team? Team { get; set; }
    public List<TournamentLineupPlayer> Lineup { get; set; } = new();
}
