using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Integração com o MatchZy de verdade (docs/plano-aws.md) — dois endpoints:
///   GET  /api/matchzy-config/{matchId}  — a EC2 carrega isso via `matchzy_loadmatch_url` no boot.
///   POST /api/matchzy-events/{matchId}  — o MatchZy chama isso via `matchzy_remote_log_url`
///                                          (cvar embutido no próprio config acima) a cada evento;
///                                          só o "map_result" nos interessa (cada EC2 = 1 mapa).
/// Exige que a API esteja alcançável pela internet (SUMMIT_PUBLIC_API_URL) — em dev, via túnel
/// (ngrok); sem essa env var, o boot cai no fluxo antigo (+map/+sv_password direto, sem MatchZy
/// configurado, sem webhook automático) pra não quebrar quem ainda não tem túnel.
/// </summary>
public static class MatchZyIntegration
{
    public static void MapMatchZyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/matchzy-config/{matchId}", async (ApiDbContext db, string matchId) =>
        {
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match == null) return Results.NotFound();

            var teamA = await db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == match.TeamAId);
            var teamB = await db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == match.TeamBId);
            if (teamA == null || teamB == null) return Results.NotFound();

            var publicApiUrl = (Environment.GetEnvironmentVariable("SUMMIT_PUBLIC_API_URL") ?? "").TrimEnd('/');

            var config = new MatchZyMatchConfigBody(
                Team1: RosterOf(teamA),
                Team2: RosterOf(teamB),
                NumMaps: 1, // nossa "série" é feita de vários boots de EC2 (um por mapa), não pelo veto interno do MatchZy
                MapList: new List<string> { MatchServerService.ToConsoleMapName(match.Map) },
                MapSides: new List<string> { "knife" }, // um valor por mapa da maplist — "knife" = round faca decide quem escolhe o lado
                ClinchSeries: true,
                PlayersPerTeam: Math.Min(5, Math.Min(Math.Max(teamA.Members.Count, 1), Math.Max(teamB.Members.Count, 1))),
                Cvars: new Dictionary<string, string>
                {
                    ["hostname"] = $"Summit: {match.TeamAName} vs {match.TeamBName}",
                    ["sv_password"] = match.ServerPassword,
                    ["matchzy_remote_log_url"] = $"{publicApiUrl}/api/matchzy-events/{match.Id}",
                    // com times de 1 jogador (teste), o cálculo padrão de mínimo pra começar pode
                    // passar de 1 e travar o warmup pra sempre com só o organizador conectado.
                    ["matchzy_minimum_ready_required"] = "1"
                });
            return Results.Ok(config);
        });

        app.MapPost("/api/matchzy-events/{matchId}", async (ApiDbContext db, string matchId, MatchZyEventBody body) =>
        {
            // o remote_log manda TODO evento (round_end, side_picked, etc.) — só nos interessa o
            // fim do mapa, que pra nós (1 EC2 = 1 mapa) já é o resultado final dessa partida.
            if (body.Event != "map_result" || body.Team1 == null || body.Team2 == null)
                return Results.Ok();

            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match == null) return Results.NotFound();
            if (match.Status == MatchStatus.Finished) return Results.Ok(new { alreadyFinished = true }); // idempotente

            async Task<List<MatchPlayerResultBody>> ToPlayersAsync(MatchZyStatsTeamBody team)
            {
                var players = new List<MatchPlayerResultBody>();
                foreach (var p in team.Players)
                {
                    var user = await db.Users.FirstOrDefaultAsync(u => u.SteamId == p.SteamId);
                    if (user == null) continue; // jogador sem conta cadastrada (ex.: espectador que trocou de time) — ignora
                    var s = p.Stats;
                    var adr = s.RoundsPlayed > 0 ? (double)s.Damage / s.RoundsPlayed : 0;
                    // MatchZy não calcula um "rating" estilo HLTV — aproximação simples a partir do que ele manda.
                    var rating = s.RoundsPlayed > 0
                        ? Math.Round((s.Kills - s.Deaths + s.Assists * 0.5 + s.Damage / 100.0) / s.RoundsPlayed, 2)
                        : 0;
                    players.Add(new MatchPlayerResultBody(user.Id, s.Kills, s.Deaths, s.Assists,
                        s.HeadshotKills, Math.Round(adr, 1), rating, s.Mvps > 0));
                }
                return players;
            }

            var resultBody = new MatchResultBody(
                body.Team1.Score, body.Team2.Score,
                DurationMinutes: Math.Max(1, (int)(DateTime.UtcNow - match.PlayedAt).TotalMinutes),
                await ToPlayersAsync(body.Team1), await ToPlayersAsync(body.Team2));

            // reusa o endpoint de resultado já testado (mesmo padrão de auto-chamada do
            // /api/debug/simulate-veto) em vez de duplicar a lógica de avanço de chave aqui.
            using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5180") };
            await http.PostAsJsonAsync($"/api/matches/{matchId}/result", resultBody);
            return Results.Ok();
        });
    }

    private static MatchZyTeamConfig RosterOf(Team team) => new(
        Name: team.Name,
        Players: team.Members
            .Where(m => !string.IsNullOrWhiteSpace(m.SteamId))
            .Take(5)
            .ToDictionary(m => m.SteamId, m => m.Nickname));
}

// ───── Config enviado pra EC2 carregar (matchzy_loadmatch_url) ─────
public record MatchZyTeamConfig(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("players")] Dictionary<string, string> Players);

public record MatchZyMatchConfigBody(
    [property: JsonPropertyName("team1")] MatchZyTeamConfig Team1,
    [property: JsonPropertyName("team2")] MatchZyTeamConfig Team2,
    [property: JsonPropertyName("num_maps")] int NumMaps,
    [property: JsonPropertyName("maplist")] List<string> MapList,
    [property: JsonPropertyName("map_sides")] List<string> MapSides,
    [property: JsonPropertyName("clinch_series")] bool ClinchSeries,
    [property: JsonPropertyName("players_per_team")] int PlayersPerTeam,
    [property: JsonPropertyName("cvars")] Dictionary<string, string> Cvars);

// ───── Eventos recebidos do MatchZy (matchzy_remote_log_url) — só o subconjunto que usamos ─────
public record MatchZyPlayerStatsBody(
    [property: JsonPropertyName("kills")] int Kills,
    [property: JsonPropertyName("deaths")] int Deaths,
    [property: JsonPropertyName("assists")] int Assists,
    [property: JsonPropertyName("headshot_kills")] int HeadshotKills,
    [property: JsonPropertyName("damage")] int Damage,
    [property: JsonPropertyName("rounds_played")] int RoundsPlayed,
    [property: JsonPropertyName("mvp")] int Mvps);

public record MatchZyStatsPlayerBody(
    [property: JsonPropertyName("steamid")] string SteamId,
    [property: JsonPropertyName("stats")] MatchZyPlayerStatsBody Stats);

public record MatchZyStatsTeamBody(
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("players")] List<MatchZyStatsPlayerBody> Players);

public record MatchZyEventBody(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("team1")] MatchZyStatsTeamBody? Team1,
    [property: JsonPropertyName("team2")] MatchZyStatsTeamBody? Team2);
