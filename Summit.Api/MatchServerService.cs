using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Provisiona servidores CS2 efêmeros na AWS (docs/plano-aws.md).
/// Config via variáveis de ambiente — nunca commitadas:
///   AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION (padrão sa-east-1),
///   SUMMIT_AMI_ID, SUMMIT_SECURITY_GROUP_ID, SUMMIT_KEY_PAIR_NAME,
///   SUMMIT_SUBNET_ID (opcional), SUMMIT_GSLT
/// </summary>
public class MatchServerService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MatchServerService> _log;

    public MatchServerService(IServiceScopeFactory scopes, ILogger<MatchServerService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUMMIT_AMI_ID"));

    /// <summary>
    /// O pool de mapas do veto usa nomes curtos ("Nuke", "Ancient"...), mas o `changelevel`/`+map`
    /// do CS2 precisa do nome real do arquivo ("de_nuke", "de_ancient"...). Bug real encontrado
    /// ao vivo em 23/jul/2026: mandar "changelevel Nuke" sem prefixo não move o mapa (o console
    /// só ecoa "int(0=0x0)"), ver docs/plano-aws.md.
    /// </summary>
    internal static string ToConsoleMapName(string map)
    {
        if (string.IsNullOrWhiteSpace(map)) return "de_mirage";
        var trimmed = map.Trim().ToLowerInvariant();
        return trimmed.StartsWith("de_") ? trimmed : $"de_{trimmed}";
    }

    private static AmazonEC2Client CreateClient()
    {
        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
        return new AmazonEC2Client(RegionEndpoint.GetBySystemName(region));
    }

    /// <summary>Pede a criação da instância. Não bloqueia — o acompanhamento é feito pelo poller.
    /// Só usado como fallback quando o pool quente (docs/plano-aws.md) está sem servidor livre.</summary>
    public async Task ProvisionAsync(string matchId)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return;

        if (!IsConfigured)
        {
            _log.LogWarning("AWS não configurada (faltam env vars) — pulando provisionamento de {MatchId}", matchId);
            return;
        }

        match.ProvisionState = ServerProvisionState.Requesting;
        await db.SaveChangesAsync();

        try
        {
            var userData = BuildUserData(match.Map, match.ServerPassword,
                Environment.GetEnvironmentVariable("SUMMIT_GSLT") ?? "");
            var instanceId = await LaunchInstanceAsync(userData, $"summit-match-{matchId}",
                new List<Tag> { new() { Key = "summit:matchId", Value = matchId } });

            match.Ec2InstanceId = instanceId;
            match.ProvisionState = ServerProvisionState.Booting;
            await db.SaveChangesAsync();
            _log.LogInformation("EC2 {InstanceId} pedida para a partida {MatchId}", instanceId, matchId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao provisionar EC2 para {MatchId}", matchId);
            match.ProvisionState = ServerProvisionState.Failed;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Pede a criação de uma instância do pool quente, parada num mapa neutro aguardando RCON.</summary>
    public async Task ProvisionPoolServerAsync()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        if (!IsConfigured) return;

        var poolServer = new PoolServer
        {
            Id = $"pool_{Guid.NewGuid():N}",
            State = PoolServerState.Booting,
            RconPassword = $"rcon_{Guid.NewGuid().ToString("N")[..12]}"
        };
        db.PoolServers.Add(poolServer);
        await db.SaveChangesAsync();

        try
        {
            var userData = BuildIdleUserData(poolServer.RconPassword,
                Environment.GetEnvironmentVariable("SUMMIT_GSLT") ?? "");
            var instanceId = await LaunchInstanceAsync(userData, $"summit-pool-{poolServer.Id}",
                new List<Tag> { new() { Key = "summit:pool", Value = "true" } });

            poolServer.Ec2InstanceId = instanceId;
            await db.SaveChangesAsync();
            _log.LogInformation("EC2 {InstanceId} pedida para o servidor do pool {PoolServerId}", instanceId, poolServer.Id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao provisionar servidor de pool {PoolServerId}", poolServer.Id);
            poolServer.State = PoolServerState.Unhealthy;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Tenta atribuir um servidor Idle do pool à partida via RCON (changelevel + senha).
    /// Retorna false se não há nenhum servidor livre — quem chamou deve cair no ProvisionAsync (cold boot).</summary>
    public async Task<bool> TryAssignFromPoolAsync(string matchId, string map, string password)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var poolServer = await db.PoolServers.FirstOrDefaultAsync(p => p.State == PoolServerState.Idle);
        if (poolServer == null) return false;

        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return false;

        var safeMap = ToConsoleMapName(map);
        try
        {
            using var rcon = new RconClient();
            if (!await rcon.ConnectAndAuthAsync(poolServer.PrivateIp, 27015, poolServer.RconPassword))
            {
                _log.LogWarning("RCON falhou ao autenticar no servidor de pool {PoolServerId} — marcando Unhealthy", poolServer.Id);
                poolServer.State = PoolServerState.Unhealthy;
                await db.SaveChangesAsync();
                return false;
            }

            await rcon.ExecCommandAsync($"sv_password {password}");
            await rcon.ExecCommandAsync($"changelevel {safeMap}");

            poolServer.State = PoolServerState.InUse;
            poolServer.CurrentMatchId = matchId;
            poolServer.AssignedAt = DateTime.UtcNow;

            match.ServerIp = $"{poolServer.PublicIp}:27015";
            match.ProvisionState = ServerProvisionState.Ready;
            match.Status = MatchStatus.Live;

            await db.SaveChangesAsync();
            _log.LogInformation("Servidor de pool {PoolServerId} atribuído à partida {MatchId} ({Map})", poolServer.Id, matchId, safeMap);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao atribuir servidor de pool {PoolServerId} à partida {MatchId}", poolServer.Id, matchId);
            poolServer.State = PoolServerState.Unhealthy;
            await db.SaveChangesAsync();
            return false;
        }
    }

    /// <summary>Devolve um servidor ao pool: reseta pra mapa neutro sem senha e marca Idle.</summary>
    public async Task ReleaseToPoolAsync(ApiDbContext db, PoolServer poolServer)
    {
        try
        {
            using var rcon = new RconClient();
            if (await rcon.ConnectAndAuthAsync(poolServer.PrivateIp, 27015, poolServer.RconPassword))
            {
                await rcon.ExecCommandAsync("sv_password \"\"");
                await rcon.ExecCommandAsync("changelevel de_dust2");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RCON falhou ao liberar servidor de pool {PoolServerId} — liberando mesmo assim", poolServer.Id);
        }

        poolServer.State = PoolServerState.Idle;
        poolServer.CurrentMatchId = null;
        poolServer.AssignedAt = null;
        _log.LogInformation("Servidor de pool {PoolServerId} liberado de volta pro pool", poolServer.Id);
    }

    /// <summary>Testa se o CS2 do servidor de pool já responde por RCON (confirmação real de "pronto pra uso").</summary>
    public async Task<bool> CheckPoolServerAliveAsync(PoolServer poolServer)
    {
        try
        {
            using var rcon = new RconClient();
            if (!await rcon.ConnectAndAuthAsync(poolServer.PrivateIp, 27015, poolServer.RconPassword, timeoutMs: 4000))
                return false;
            await rcon.ExecCommandAsync("status");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CheckPoolServerAliveAsync falhou pra {PoolServerId}", poolServer.Id);
            return false;
        }
    }

    /// <summary>Conta jogadores humanos conectados via RCON "status" (best-effort — formato pode variar por versão).</summary>
    public async Task<int?> GetHumanPlayerCountAsync(PoolServer poolServer)
    {
        try
        {
            using var rcon = new RconClient();
            if (!await rcon.ConnectAndAuthAsync(poolServer.PrivateIp, 27015, poolServer.RconPassword, timeoutMs: 4000))
                return null;
            var status = await rcon.ExecCommandAsync("status");
            // linha típica: "players : 2 humans, 0 bots (10 max) (not hibernating)"
            var match = System.Text.RegularExpressions.Regex.Match(status, @"(\d+)\s+humans?");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sobe uma instância pura da AMI, sem nenhum User Data — pra teste manual via SSH.</summary>
    public Task<string> LaunchBareInstanceAsync()
        => LaunchInstanceAsync("#!/bin/bash\n# instância manual — sem boot automático de CS2\n",
            "summit-manual-test", new List<Tag> { new() { Key = "summit:manual", Value = "true" } });

    private async Task<string> LaunchInstanceAsync(string userData, string nameTag, List<Tag> extraTags)
    {
        using var ec2 = CreateClient();
        var securityGroupId = Environment.GetEnvironmentVariable("SUMMIT_SECURITY_GROUP_ID")
            ?? throw new InvalidOperationException("SUMMIT_SECURITY_GROUP_ID não configurada.");

        var tags = new List<Tag> { new() { Key = "Name", Value = nameTag } };
        tags.AddRange(extraTags);

        var request = new RunInstancesRequest
        {
            ImageId = Environment.GetEnvironmentVariable("SUMMIT_AMI_ID"),
            InstanceType = InstanceType.C5Large,
            MinCount = 1,
            MaxCount = 1,
            KeyName = Environment.GetEnvironmentVariable("SUMMIT_KEY_PAIR_NAME"),
            SecurityGroupIds = new List<string> { securityGroupId },
            UserData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userData)),
            InstanceInitiatedShutdownBehavior = ShutdownBehavior.Terminate,
            TagSpecifications = new List<TagSpecification>
            {
                new() { ResourceType = ResourceType.Instance, Tags = tags }
            }
        };

        var subnetId = Environment.GetEnvironmentVariable("SUMMIT_SUBNET_ID");
        if (!string.IsNullOrWhiteSpace(subnetId)) request.SubnetId = subnetId;

        var response = await ec2.RunInstancesAsync(request);
        return response.Reservation.Instances.First().InstanceId;
    }

    /// <summary>Chamado pelo ServerProvisionPoller: consulta o IP e atualiza a sala quando pronta;
    /// se já está Ready mas o MatchZy ainda não carregou o config, tenta de novo via RCON.</summary>
    public async Task<bool> PollAsync(ApiDbContext db, Match match)
    {
        if (string.IsNullOrEmpty(match.Ec2InstanceId)) return false;

        if (match.ProvisionState == ServerProvisionState.Ready)
            return await TryLoadMatchZyConfigAsync(match);

        using var ec2 = CreateClient();
        var desc = await ec2.DescribeInstancesAsync(new DescribeInstancesRequest
        {
            InstanceIds = new List<string> { match.Ec2InstanceId }
        });

        var instance = desc.Reservations.SelectMany(r => r.Instances).FirstOrDefault();
        if (instance == null) return false;

        if (instance.State.Name == InstanceStateName.Running && !string.IsNullOrEmpty(instance.PublicIpAddress))
        {
            match.ServerIp = $"{instance.PublicIpAddress}:27015";
            match.ServerPrivateIp = instance.PrivateIpAddress ?? string.Empty;
            match.ProvisionState = ServerProvisionState.Ready;
            match.Status = MatchStatus.Live;
            _log.LogInformation("Servidor pronto para {MatchId}: {Ip}", match.Id, match.ServerIp);
            await TryLoadMatchZyConfigAsync(match); // pode falhar nessa mesma tick (CS2 ainda subindo) — o poller tenta de novo em 10s
            return true;
        }

        if (instance.State.Name == InstanceStateName.Terminated || instance.State.Name == InstanceStateName.ShuttingDown)
        {
            match.ProvisionState = ServerProvisionState.Failed;
            return true;
        }

        return false;
    }

    /// <summary>Manda `matchzy_loadmatch_url` via RCON pro servidor da sala — não pode ir na linha
    /// de comando do CS2 (ver comentário em BuildUserData). Idempotente via Match.MatchZyConfigLoaded.</summary>
    private async Task<bool> TryLoadMatchZyConfigAsync(Match match)
    {
        if (match.MatchZyConfigLoaded) return false;
        var publicApiUrl = Environment.GetEnvironmentVariable("SUMMIT_PUBLIC_API_URL")?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicApiUrl) || string.IsNullOrEmpty(match.ServerIp)) return false;

        var ip = string.IsNullOrEmpty(match.ServerPrivateIp) ? match.ServerIp.Split(':')[0] : match.ServerPrivateIp;
        try
        {
            using var rcon = new RconClient();
            if (!await rcon.ConnectAndAuthAsync(ip, 27015, match.ServerPassword, timeoutMs: 4000))
                return false; // CS2 provavelmente ainda não subiu — tenta de novo no próximo tick

            await rcon.ExecCommandAsync($"matchzy_loadmatch_url \"{publicApiUrl}/api/matchzy-config/{match.Id}\"");
            match.MatchZyConfigLoaded = true;
            _log.LogInformation("Config do MatchZy carregado via RCON pra {MatchId}", match.Id);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao carregar config do MatchZy via RCON pra {MatchId} — tenta de novo no próximo tick", match.Id);
            return false;
        }
    }

    /// <summary>Chamado pelo PoolManagerService: consulta o IP da instância de pool quando ainda Booting.</summary>
    public async Task<bool> PollPoolServerAsync(PoolServer poolServer)
    {
        if (string.IsNullOrEmpty(poolServer.Ec2InstanceId) || !string.IsNullOrEmpty(poolServer.PublicIp))
            return false;

        using var ec2 = CreateClient();
        var desc = await ec2.DescribeInstancesAsync(new DescribeInstancesRequest
        {
            InstanceIds = new List<string> { poolServer.Ec2InstanceId }
        });
        var instance = desc.Reservations.SelectMany(r => r.Instances).FirstOrDefault();
        if (instance == null) return false;

        if (instance.State.Name == InstanceStateName.Running && !string.IsNullOrEmpty(instance.PublicIpAddress))
        {
            poolServer.PublicIp = instance.PublicIpAddress;
            poolServer.PrivateIp = instance.PrivateIpAddress ?? string.Empty;
            return true;
        }
        if (instance.State.Name == InstanceStateName.Terminated || instance.State.Name == InstanceStateName.ShuttingDown)
        {
            poolServer.State = PoolServerState.Unhealthy;
            return true;
        }
        return false;
    }

    public async Task TerminateAsync(string ec2InstanceId)
    {
        using var ec2 = CreateClient();
        await ec2.TerminateInstancesAsync(new TerminateInstancesRequest
        {
            InstanceIds = new List<string> { ec2InstanceId }
        });
    }

    /// <summary>Stop (não Terminate) — preserva o disco/instalação, pode dar StartInstances depois.</summary>
    public async Task StopAsync(string ec2InstanceId)
    {
        using var ec2 = CreateClient();
        await ec2.StopInstancesAsync(new StopInstancesRequest
        {
            InstanceIds = new List<string> { ec2InstanceId }
        });
    }

    /// <summary>
    /// Script de boot (EC2 User Data): sobe o CS2 já no mapa definido pelo veto.
    /// Assume a AMI summit-cs2-v1 (CS2 + Metamod + screen instalados em ~/cs2).
    /// CRÍTICO #1: precisa rodar dentro de um `screen` (pseudo-TTY) — sem terminal
    /// alocado o processo do CS2 trava mudo logo após carregar libv8system.so.
    /// CRÍTICO #2: a AMI foi clonada com /etc/machine-id fixo (gravado no snapshot) —
    /// toda instância nascida dela tinha a MESMA identidade de máquina, o que travava
    /// silenciosamente a autenticação Steam (mesmo sintoma do #1: trava mudo logo após
    /// libv8system.so). Regenerar o machine-id a partir da UUID real da VM (única por
    /// instância) antes de subir o CS2 resolve. Ambos validados ao vivo em 22-23/jul/2026,
    /// ver docs/plano-aws.md.
    /// </summary>
    /// <summary>
    /// Sempre sobe no mapa do veto normalmente (`+map`) — `matchzy_loadmatch_url` NÃO pode ir na
    /// linha de comando do CS2: testado ao vivo em 15/ago/2026 e dá
    /// `[MatchZy] [LoadMatchFromURL - FATAL] Entity system yet is not initialized` (dispara antes
    /// do motor terminar de inicializar). Com SUMMIT_PUBLIC_API_URL configurada, o boot também
    /// define `rcon_password` (mesmo valor da senha da sala, só pra essa instância efêmera) —
    /// o `ServerProvisionPoller`/`PollAsync` manda o `matchzy_loadmatch_url` de verdade via RCON
    /// depois que o servidor confirma Ready (ver `TryLoadMatchZyConfigAsync`).
    /// </summary>
    private static string BuildUserData(string map, string password, string gslt)
    {
        var publicApiUrl = Environment.GetEnvironmentVariable("SUMMIT_PUBLIC_API_URL");
        var rconArg = string.IsNullOrWhiteSpace(publicApiUrl) ? "" : $" +rcon_password {password}";
        var startCmd = $"./bin/linuxsteamrt64/cs2 -dedicated -port 27015 +sv_setsteamaccount {gslt} +map {ToConsoleMapName(map)} +sv_password {password}{rconArg}";
        return $$"""
        #!/bin/bash
        rm -f /etc/machine-id
        systemd-machine-id-setup
        systemctl restart dbus
        su ubuntu -c '
        cd /home/ubuntu/cs2/game
        export LD_LIBRARY_PATH="$PWD/bin/linuxsteamrt64:$PWD/csgo/bin/linuxsteamrt64"
        screen -dmS cs2server bash -c "{{startCmd}} 2>&1 | tee /home/ubuntu/match_server.log"
        '
        """;
    }

    /// <summary>
    /// Script de boot pra servidor de pool "quente" (docs/plano-aws.md): sobe o CS2 num mapa
    /// neutro, sem senha, com rcon_password fixo — fica esperando ser atribuído a uma partida
    /// via RCON (changelevel + sv_password), sem precisar de outro boot de EC2.
    /// </summary>
    private static string BuildIdleUserData(string rconPassword, string gslt)
    {
        return $$"""
        #!/bin/bash
        rm -f /etc/machine-id
        systemd-machine-id-setup
        systemctl restart dbus
        su ubuntu -c '
        cd /home/ubuntu/cs2/game
        export LD_LIBRARY_PATH="$PWD/bin/linuxsteamrt64:$PWD/csgo/bin/linuxsteamrt64"
        screen -dmS cs2server bash -c "./bin/linuxsteamrt64/cs2 -dedicated -port 27015 +sv_setsteamaccount {{gslt}} +map de_dust2 +rcon_password {{rconPassword}} 2>&1 | tee /home/ubuntu/match_server.log"
        '
        """;
    }
}
