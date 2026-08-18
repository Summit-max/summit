namespace Summit.Models;

// Preparando (instância pedida) -> Booting (ligou, aguardando IP) -> Ready (IP obtido, CS2 a caminho) -> Failed
public enum ServerProvisionState { None = 0, Requesting = 1, Booting = 2, Ready = 3, Failed = 4 }

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

    // Série (MD1/MD3/MD5): qual mapa desta série este registro representa (1-based).
    // Vários Match podem compartilhar o mesmo BracketMatchId — um por mapa jogado.
    public int GameNumber { get; set; } = 1;

    // Sala da partida (preenchida quando o veto termina)
    public string ServerIp { get; set; } = string.Empty;
    public string ServerPassword { get; set; } = string.Empty;
    // IP privado da instância — usado só pro RCON interno (API e servidor CS2 na mesma VPC,
    // conectar via ServerIp/público de dentro da VPC não funciona). ServerIp continua sendo
    // o certo pro connect string do jogador.
    public string ServerPrivateIp { get; set; } = string.Empty;

    // Provisionamento AWS (efêmero) — instância criada por partida, terminada ao fim
    public string? Ec2InstanceId { get; set; }
    public ServerProvisionState ProvisionState { get; set; } = ServerProvisionState.None;

    // matchzy_loadmatch_url não pode ir na linha de comando do CS2 (dispara cedo demais, antes do
    // entity system inicializar — "Entity system yet is not initialized") — o ServerProvisionPoller
    // manda via RCON depois que o servidor confirma Ready; este campo evita re-envio a cada tick.
    public bool MatchZyConfigLoaded { get; set; }

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
