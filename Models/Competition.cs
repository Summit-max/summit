namespace Summit.Models;

// ───── Formatos de campeonato (espec-campeonatos.md §1, §6) ─────
public enum TournamentFormat
{
    SingleElimination = 0,
    DoubleElimination = 1,
    Swiss = 2
}

public enum SeriesFormat
{
    MD1 = 0,
    MD3 = 1,
    MD5 = 2
}

// ───── Check-in (§4) ─────
public enum CheckInStatus
{
    Waiting = 0,
    Confirmed = 1,
    NoShow = 2
}

// ───── Vetos (§8) ─────
public enum VetoActionType
{
    Ban = 0,
    Pick = 1,
    Decider = 2
}

public class VetoSession
{
    public string Id { get; set; } = string.Empty;
    public string BracketMatchId { get; set; } = string.Empty;
    public SeriesFormat Series { get; set; } = SeriesFormat.MD1;
    public string MapPoolCsv { get; set; } = string.Empty;
    public string TeamATag { get; set; } = string.Empty;
    public string TeamBTag { get; set; } = string.Empty;
    public int StepIndex { get; set; }
    public bool IsComplete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<VetoStep> Steps { get; set; } = new();

    public List<string> MapPool => string.IsNullOrWhiteSpace(MapPoolCsv)
        ? new List<string>()
        : MapPoolCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public class VetoStep
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int Order { get; set; }
    public string TeamTag { get; set; } = string.Empty;   // quem executou ("—" no decider)
    public VetoActionType Action { get; set; }
    public string Map { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public VetoSession? Session { get; set; }
}

// ───── Solicitação de entrada no time (espec-times.md §8) ─────
public enum JoinRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3,
    Expired = 4
}

public class TeamJoinRequest
{
    public string Id { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }

    public Team? Team { get; set; }
    public User? User { get; set; }
}

// ───── Escalação por campeonato (espec-times.md §16-21) ─────
public class TournamentLineupPlayer
{
    public string Id { get; set; } = string.Empty;
    public string TournamentTeamId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    public TournamentTeam? TournamentTeam { get; set; }
    public User? User { get; set; }
}

// ───── Auditoria (espec-times.md §32; espec-campeonatos.md Administração) ─────
public class AuditLog
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? TeamId { get; set; }
    public string? TournamentId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ───── DTOs do estado do veto (GET /api/veto/{bmId}) ─────
public class VetoState
{
    public VetoSession? Session { get; set; }
    public List<string> Remaining { get; set; } = new();
    public VetoNext? Next { get; set; }
}

public class VetoNext
{
    public string Action { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
}
