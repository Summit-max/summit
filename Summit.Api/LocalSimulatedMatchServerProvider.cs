using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Implementação padrão de <see cref="IMatchServerProvider"/> — não fala com a AWS em nenhum
/// momento. "Provisiona" uma sala com IP simulado em segundos e, depois de outro delay
/// configurável, produz um resultado sozinha (placar + estatísticas geradas), completando o
/// ciclo de vida inteiro da partida pra que o resto do pipeline (avanço de chave, stats, badges,
/// docs/spec/summit-fase-final/plan.md RF-01 em diante) seja exercitado sem nenhuma dependência
/// externa. Ver RF-00 do plan.md.
/// </summary>
public class LocalSimulatedMatchServerProvider : IMatchServerProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LocalSimulatedMatchServerProvider> _log;

    // instrução de teste dirigido — POST /api/debug/simulate-result/{matchId} grava aqui antes
    // do delay automático disparar, pra permitir andar a chave de propósito num teste manual.
    private static readonly Dictionary<string, char> PendingWinnerOverride = new();

    public LocalSimulatedMatchServerProvider(IServiceScopeFactory scopes, ILogger<LocalSimulatedMatchServerProvider> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public static void SetWinnerOverride(string matchId, char side) => PendingWinnerOverride[matchId] = side;

    /// <summary>Dev: força o resultado AGORA, sem esperar o delay automático — usado pelo
    /// endpoint /api/debug/force-match-result (docs/spec/summit-fase-final, ferramenta de teste
    /// pra "bypassar" partida manualmente).</summary>
    public async Task ForceResultNowAsync(string matchId, char? forcedWinner)
    {
        if (forcedWinner.HasValue) SetWinnerOverride(matchId, forcedWinner.Value);
        await PostSimulatedResultAsync(matchId);
    }

    private static int DelaySeconds(string envVar, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(envVar), out var n) && n >= 0 ? n : fallback;

    public async Task ProvisionAsync(string matchId)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return;

        await Task.Delay(TimeSpan.FromSeconds(DelaySeconds("SUMMIT_SIM_DELAY_SECONDS", 5)));

        match.ServerIp = "sim.summit.local:27015";
        match.ProvisionState = ServerProvisionState.Ready;
        match.Status = MatchStatus.Live;
        await db.SaveChangesAsync();
        _log.LogInformation("[sim] Sala simulada pronta pra partida {MatchId}", matchId);

        _ = ScheduleSimulatedResultAsync(matchId);
    }

    public async Task<bool> TryAssignFromPoolAsync(string matchId, string map, string password)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (match == null) return false;

        match.ServerIp = "sim.summit.local:27015";
        match.ProvisionState = ServerProvisionState.Ready;
        match.Status = MatchStatus.Live;
        await db.SaveChangesAsync();
        _log.LogInformation("[sim] \"Pool\" simulado atribuído na hora pra partida {MatchId}", matchId);

        _ = ScheduleSimulatedResultAsync(matchId);
        return true;
    }

    private async Task ScheduleSimulatedResultAsync(string matchId)
    {
        await Task.Delay(TimeSpan.FromSeconds(DelaySeconds("SUMMIT_SIM_RESULT_DELAY_SECONDS", 20)));
        await PostSimulatedResultAsync(matchId);
    }

    private async Task PostSimulatedResultAsync(string matchId)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var match = await db.Matches.Include(m => m.Players)
                .FirstOrDefaultAsync(m => m.Id == matchId);
            if (match == null || match.Status == MatchStatus.Finished) return;

            var teamA = await db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == match.TeamAId);
            var teamB = await db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == match.TeamBId);

            var aWins = PendingWinnerOverride.TryGetValue(matchId, out var side)
                ? side == 'A'
                : Random.Shared.Next(2) == 0;
            PendingWinnerOverride.Remove(matchId);

            var scoreA = aWins ? 16 : Random.Shared.Next(4, 15);
            var scoreB = aWins ? Random.Shared.Next(4, 15) : 16;

            var body = new
            {
                ScoreA = scoreA,
                ScoreB = scoreB,
                DurationMinutes = 35 + Random.Shared.Next(0, 20),
                PlayersA = BuildSimulatedRoster(teamA, aWins),
                PlayersB = BuildSimulatedRoster(teamB, !aWins)
            };

            using var http = new HttpClient
            { BaseAddress = new Uri($"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "5180"}") };
            var resp = await http.PostAsJsonAsync($"/api/matches/{matchId}/result", body);
            _log.LogInformation("[sim] Resultado simulado enviado pra {MatchId}: {A}x{B} — status {Status}",
                matchId, scoreA, scoreB, resp.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[sim] Falha ao simular resultado de {MatchId}", matchId);
        }
    }

    private static List<object> BuildSimulatedRoster(Team? team, bool won)
    {
        var members = (team?.Members ?? new List<User>())
            .OrderBy(m => m.TeamJoinedAt ?? DateTime.MaxValue)
            .Take(5)
            .ToList();
        if (members.Count == 0) return new List<object>();

        var mvpIndex = won ? Random.Shared.Next(members.Count) : -1;
        return members.Select((m, i) =>
        {
            var kills = Random.Shared.Next(8, 28);
            var deaths = Random.Shared.Next(10, 22);
            return (object)new
            {
                UserId = m.Id,
                Kills = kills,
                Deaths = deaths,
                Assists = Random.Shared.Next(2, 10),
                HeadshotKills = (int)(kills * (0.35 + Random.Shared.NextDouble() * 0.35)),
                AvgDamagePerRound = Math.Round(55 + Random.Shared.NextDouble() * 50, 1),
                Rating = Math.Round(0.6 + Random.Shared.NextDouble() * 1.0, 2),
                IsMvp = i == mvpIndex
            };
        }).ToList();
    }
}
