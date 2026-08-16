using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Mantém o pool de servidores CS2 "quentes" (docs/plano-aws.md) — repõe até SUMMIT_POOL_SIZE,
/// confirma via RCON que um servidor Booting realmente está pronto antes de marcar Idle, e
/// libera automaticamente servidores InUse que ficaram vazios. Tick 30s.
/// </summary>
public class PoolManagerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PoolManagerService> _log;
    private static readonly TimeSpan AssignGrace = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan HardReleaseCeiling = TimeSpan.FromHours(3);

    public PoolManagerService(IServiceScopeFactory scopes, ILogger<PoolManagerService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    private static int PoolSize =>
        int.TryParse(Environment.GetEnvironmentVariable("SUMMIT_POOL_SIZE"), out var n) && n >= 0 ? n : 1;

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

                    await TopUpAsync(db, server, ct);
                    await ConfirmBootingAsync(db, server, ct);
                    await ReleaseEmptyAsync(db, server, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Erro no PoolManagerService");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task TopUpAsync(ApiDbContext db, MatchServerService server, CancellationToken ct)
    {
        var alive = await db.PoolServers.CountAsync(p => p.State != PoolServerState.Unhealthy, ct);
        var missing = PoolSize - alive;
        for (var i = 0; i < missing; i++)
            await server.ProvisionPoolServerAsync();
    }

    private async Task ConfirmBootingAsync(ApiDbContext db, MatchServerService server, CancellationToken ct)
    {
        var booting = await db.PoolServers.Where(p => p.State == PoolServerState.Booting).ToListAsync(ct);
        foreach (var p in booting)
        {
            if (string.IsNullOrEmpty(p.PublicIp))
            {
                var changed = await server.PollPoolServerAsync(p);
                if (changed) await db.SaveChangesAsync(ct);
                continue;
            }

            // IP já respondendo na AWS — só considera "pronto" quando o CS2 responde de verdade por RCON
            if (await server.CheckPoolServerAliveAsync(p))
            {
                p.State = PoolServerState.Idle;
                await db.SaveChangesAsync(ct);
                _log.LogInformation("Servidor de pool {PoolServerId} confirmado Idle (RCON respondeu)", p.Id);
            }
        }
    }

    private async Task ReleaseEmptyAsync(ApiDbContext db, MatchServerService server, CancellationToken ct)
    {
        var inUse = await db.PoolServers.Where(p => p.State == PoolServerState.InUse).ToListAsync(ct);
        foreach (var p in inUse)
        {
            if (p.AssignedAt == null) continue;
            var elapsed = DateTime.UtcNow - p.AssignedAt.Value;
            if (elapsed < AssignGrace) continue;

            if (elapsed > HardReleaseCeiling)
            {
                await server.ReleaseToPoolAsync(db, p);
                await db.SaveChangesAsync(ct);
                continue;
            }

            var humans = await server.GetHumanPlayerCountAsync(p);
            if (humans == 0)
            {
                await server.ReleaseToPoolAsync(db, p);
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
