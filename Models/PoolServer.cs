namespace Summit.Models;

// Booting (EC2 pedida) -> Idle (CS2 rodando, RCON confirmado, pronto pra uso) ->
// InUse (atribuído a uma partida) -> volta pra Idle quando esvazia. Unhealthy = RCON parou de responder.
public enum PoolServerState { Booting = 0, Idle = 1, InUse = 2, Unhealthy = 3 }

public class PoolServer
{
    public string Id { get; set; } = string.Empty;
    public string Ec2InstanceId { get; set; } = string.Empty;
    public string PublicIp { get; set; } = string.Empty;
    // usado só pro RCON interno — API e servidor CS2 ficam na mesma VPC, e conectar via
    // PublicIp de dentro da própria VPC não funciona (mesmo problema que o RDS teve com
    // "Acesso público"). PublicIp continua sendo o certo pro connect string do jogador.
    public string PrivateIp { get; set; } = string.Empty;
    public PoolServerState State { get; set; } = PoolServerState.Booting;
    public string RconPassword { get; set; } = string.Empty;
    public string? CurrentMatchId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
