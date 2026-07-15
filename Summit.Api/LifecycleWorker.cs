using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Motor do ciclo de vida dos campeonatos (espec-campeonatos.md):
/// T-30min fecha o check-in e remove ausentes → gera a chave com os
/// confirmados → T-0 inicia o campeonato e abre os vetos da 1ª rodada.
/// Roda a cada 20 segundos.
/// </summary>
public class LifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;

    public LifecycleWorker(IServiceScopeFactory scopes) => _scopes = scopes;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                await TickAsync(db);
            }
            catch { /* nunca derruba o worker */ }
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
        }
    }

    private static async Task TickAsync(ApiDbContext db)
    {
        var now = DateTime.UtcNow;
        var tours = await db.Tournaments
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team)
            .Include(t => t.Bracket).ThenInclude(r => r.Matches)
            .Where(t => t.Status == TournamentStatus.Open || t.Status == TournamentStatus.Upcoming)
            .ToListAsync();

        foreach (var t in tours)
        {
            // ── T-30min: fecha check-in (remove ausentes) e gera a chave ──
            if (now >= t.CheckInClosesAt)
            {
                var waiting = t.TournamentTeams
                    .Where(x => x.CheckIn == CheckInStatus.Waiting && !x.IsEliminated)
                    .ToList();
                foreach (var w in waiting)
                {
                    w.CheckIn = CheckInStatus.NoShow;
                    w.IsEliminated = true;
                    await CompetitionEndpoints.Audit(db, "team_noshow_removed", null, null,
                        w.TeamId, t.Id, null, null, "Não realizou check-in (automático)");
                }

                if (t.Bracket.Count == 0)
                {
                    var confirmed = t.TournamentTeams
                        .Where(x => x.CheckIn == CheckInStatus.Confirmed && x.Team != null)
                        .OrderBy(x => x.Seed)
                        .ToList();
                    if (confirmed.Count >= 2)
                    {
                        GenerateBracket(db, t, confirmed);
                        await CompetitionEndpoints.Audit(db, "bracket_generated", null, null,
                            null, t.Id, null, $"{confirmed.Count} times", null);
                    }
                }
            }

            // ── T-0: inicia o campeonato e abre os vetos da 1ª rodada ──
            if (now >= t.StartDate)
            {
                // fallback: se ninguém confirmou check-in, usa os inscritos
                if (t.Bracket.Count == 0)
                {
                    var teams = t.TournamentTeams
                        .Where(x => !x.IsEliminated && x.Team != null)
                        .OrderBy(x => x.Seed)
                        .ToList();
                    if (teams.Count >= 2) GenerateBracket(db, t, teams);
                }

                if (t.Bracket.Count == 0) continue; // sem times suficientes, aguarda

                t.Status = TournamentStatus.InProgress;

                var r1 = t.Bracket.OrderBy(r => r.RoundNumber).First();
                foreach (var bm in r1.Matches.Where(m =>
                             m.TeamATag != "TBD" && m.TeamBTag != "TBD" &&
                             m.Status == BracketMatchStatus.Pending))
                {
                    var hasVeto = await db.VetoSessions.AnyAsync(v => v.BracketMatchId == bm.Id);
                    if (hasVeto) continue;
                    db.VetoSessions.Add(new VetoSession
                    {
                        Id = $"veto_{Guid.NewGuid():N}",
                        BracketMatchId = bm.Id,
                        Series = t.Series,
                        MapPoolCsv = t.MapPoolCsv,
                        TeamATag = bm.TeamATag,
                        TeamBTag = bm.TeamBTag
                    });
                    bm.Status = BracketMatchStatus.Veto;
                }

                await CompetitionEndpoints.Audit(db, "tournament_started", null, null,
                    null, t.Id, null, null, "Countdown chegou a zero");
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Chave de eliminação simples a partir dos times presentes (seed ordena).</summary>
    private static void GenerateBracket(ApiDbContext db, Tournament t, List<TournamentTeam> teams)
    {
        int n = teams.Count;
        int totalRounds = (int)Math.Ceiling(Math.Log2(Math.Max(n, 2)));

        static string RoundName(int index, int total) => (total - index) switch
        {
            1 => "FINAL",
            2 => "SEMIS",
            3 => "QUARTAS",
            _ => $"RODADA {index + 1}"
        };

        var rounds = new List<BracketRound>();
        for (int r = 0; r < totalRounds; r++)
        {
            var round = new BracketRound
            {
                Id = $"rnd_{Guid.NewGuid():N}",
                TournamentId = t.Id,
                RoundNumber = r + 1,
                Name = RoundName(r, totalRounds)
            };
            rounds.Add(round);
            db.BracketRounds.Add(round);

            int matchesInRound = (int)Math.Ceiling(n / Math.Pow(2, r + 1));
            for (int p = 0; p < Math.Max(matchesInRound, 1); p++)
            {
                var bm = new BracketMatch
                {
                    Id = $"bm_{Guid.NewGuid():N}",
                    RoundId = round.Id,
                    Position = p + 1,
                    TeamATag = "TBD",
                    TeamBTag = "TBD",
                    Status = BracketMatchStatus.Pending,
                    ScheduledAt = t.StartDate.AddHours(r)
                };
                if (r == 0)
                {
                    var a = p * 2 < n ? teams[p * 2] : null;
                    var b = p * 2 + 1 < n ? teams[p * 2 + 1] : null;
                    bm.TeamATag = a?.Team?.Tag ?? "TBD";
                    bm.TeamBTag = b?.Team?.Tag ?? "BYE";
                }
                db.BracketMatches.Add(bm);
            }
        }
    }
}
