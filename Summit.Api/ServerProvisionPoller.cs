using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

/// <summary>Fica de olho nas partidas "Booting" e grava o IP assim que a EC2 responde. Tick 10s.</summary>
public class ServerProvisionPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ServerProvisionPoller> _log;

    public ServerProvisionPoller(IServiceScopeFactory scopes, ILogger<ServerProvisionPoller> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (MatchServerService.IsConfigured)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                    var server = scope.ServiceProvider.GetRequiredService<MatchServerService>();

                    // Booting: ainda esperando IP. Ready+!MatchZyConfigLoaded: já tem IP, mas o
                    // matchzy_loadmatch_url via RCON pode ter falhado (CS2 ainda subindo) — retenta.
                    var pending = await db.Matches
                        .Where(m => m.Status != MatchStatus.Finished
                                 && (m.ProvisionState == ServerProvisionState.Booting
                                     || (m.ProvisionState == ServerProvisionState.Ready && !m.MatchZyConfigLoaded)))
                        .ToListAsync(ct);

                    foreach (var m in pending)
                    {
                        var changed = await server.PollAsync(db, m);
                        if (changed) await db.SaveChangesAsync(ct);
                    }

                    await CleanupStaleServersAsync(db, server, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Erro no ServerProvisionPoller");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    /// <summary>Rede de segurança: uma sala com servidor dedicado (não veio do pool) que nunca
    /// reportou resultado — por bug, W.O. sem detecção, ou alguém só sumindo — ficaria com a
    /// EC2 ligada pra sempre (foi exatamente o que aconteceu no teste de 19/ago/2026, quase
    /// US$3 de EC2 rodando ~24h à toa). Depois de um teto de horas sem terminar, derruba e marca
    /// Cancelled em vez de deixar vazando.</summary>
    private static readonly TimeSpan StaleServerCeiling = TimeSpan.FromHours(3);

    private static async Task CleanupStaleServersAsync(ApiDbContext db, MatchServerService server, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - StaleServerCeiling;
        var stale = await db.Matches
            .Where(m => m.Status != MatchStatus.Finished && m.Status != MatchStatus.Cancelled
                     && m.Ec2InstanceId != null && m.Ec2InstanceId != "" && m.PlayedAt < cutoff)
            .ToListAsync(ct);

        foreach (var m in stale)
        {
            await server.TerminateAsync(m.Ec2InstanceId!);
            m.Status = MatchStatus.Cancelled;
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }
}
