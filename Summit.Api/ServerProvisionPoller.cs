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

                    var booting = await db.Matches
                        .Where(m => m.ProvisionState == ServerProvisionState.Booting)
                        .ToListAsync(ct);

                    foreach (var m in booting)
                    {
                        var changed = await server.PollAsync(db, m);
                        if (changed) await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Erro no ServerProvisionPoller");
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}
