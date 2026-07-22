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

    private static AmazonEC2Client CreateClient()
    {
        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
        return new AmazonEC2Client(RegionEndpoint.GetBySystemName(region));
    }

    /// <summary>Pede a criação da instância. Não bloqueia — o acompanhamento é feito pelo poller.</summary>
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
            using var ec2 = CreateClient();
            var userData = BuildUserData(match.Map, match.ServerPassword,
                Environment.GetEnvironmentVariable("SUMMIT_GSLT") ?? "");

            var securityGroupId = Environment.GetEnvironmentVariable("SUMMIT_SECURITY_GROUP_ID")
                ?? throw new InvalidOperationException("SUMMIT_SECURITY_GROUP_ID não configurada.");

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
                    new()
                    {
                        ResourceType = ResourceType.Instance,
                        Tags = new List<Tag>
                        {
                            new() { Key = "Name", Value = $"summit-match-{matchId}" },
                            new() { Key = "summit:matchId", Value = matchId }
                        }
                    }
                }
            };

            var subnetId = Environment.GetEnvironmentVariable("SUMMIT_SUBNET_ID");
            if (!string.IsNullOrWhiteSpace(subnetId)) request.SubnetId = subnetId;

            var response = await ec2.RunInstancesAsync(request);
            var instanceId = response.Reservation.Instances.First().InstanceId;

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

    /// <summary>Chamado pelo ServerProvisionPoller: consulta o IP e atualiza a sala quando pronta.</summary>
    public async Task<bool> PollAsync(ApiDbContext db, Match match)
    {
        if (string.IsNullOrEmpty(match.Ec2InstanceId)) return false;

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
            match.ProvisionState = ServerProvisionState.Ready;
            match.Status = MatchStatus.Live;
            _log.LogInformation("Servidor pronto para {MatchId}: {Ip}", match.Id, match.ServerIp);
            return true;
        }

        if (instance.State.Name == InstanceStateName.Terminated || instance.State.Name == InstanceStateName.ShuttingDown)
        {
            match.ProvisionState = ServerProvisionState.Failed;
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

    /// <summary>
    /// Script de boot (EC2 User Data): espera a rede, ajusta as libs e sobe o CS2 já no mapa
    /// definido pelo veto. Assume a AMI summit-cs2-v1 (CS2 + Metamod instalados em ~/cs2).
    /// </summary>
    private static string BuildUserData(string map, string password, string gslt)
    {
        var safeMap = string.IsNullOrWhiteSpace(map) ? "de_mirage" : map.Trim();
        return $$"""
        #!/bin/bash
        set -e
        cd /home/ubuntu/cs2/game
        export LD_LIBRARY_PATH="$PWD/bin/linuxsteamrt64:$PWD/csgo/bin/linuxsteamrt64:$LD_LIBRARY_PATH"
        su ubuntu -c "cd /home/ubuntu/cs2/game && \
          export LD_LIBRARY_PATH=\"$PWD/bin/linuxsteamrt64:$PWD/csgo/bin/linuxsteamrt64\" && \
          ./bin/linuxsteamrt64/cs2 -dedicated -port 27015 \
            +sv_setsteamaccount {{gslt}} \
            +map {{safeMap}} \
            +sv_password {{password}} \
            > /home/ubuntu/match_server.log 2>&1 &"
        """;
    }
}
