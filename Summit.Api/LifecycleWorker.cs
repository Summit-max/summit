using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
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
            // ── T-1h: check-in abre — avisa o capitão de cada time inscrito (uma vez só) ──
            if (now >= t.CheckInOpensAt && now < t.CheckInClosesAt)
            {
                var alreadyNotified = await db.Notifications.AnyAsync(n =>
                    n.Type == NotificationType.CheckInOpened && n.RelatedId == t.Id);
                if (!alreadyNotified)
                {
                    foreach (var tt in t.TournamentTeams.Where(x => !x.IsEliminated))
                    {
                        var captainId = tt.CaptainUserId ?? tt.Team?.CaptainId;
                        if (!string.IsNullOrEmpty(captainId))
                            await NotificationHelper.Notify(db, captainId, NotificationType.CheckInOpened,
                                $"Check-in aberto pra {t.Name} — confirme presença antes de {t.CheckInClosesAt:HH:mm}.", t.Id);
                    }
                }
            }

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
                        await GenerateBracket(db, t, confirmed);
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
                    if (teams.Count >= 2) await GenerateBracket(db, t, teams);
                }

                if (t.Bracket.Count == 0) continue; // sem times suficientes, aguarda

                t.Status = TournamentStatus.InProgress;

                var r1 = t.Bracket.OrderBy(r => r.RoundNumber).First();
                foreach (var bm in r1.Matches)
                    await CompetitionEndpoints.OpenVetoForMatchAsync(db, t.Id, bm);

                await CompetitionEndpoints.Audit(db, "tournament_started", null, null,
                    null, t.Id, null, null, "Countdown chegou a zero");
            }
        }

        await CheckLoyaltyBadgesAsync(db);
        await CheckVetoNoShowsAsync(db);

        await db.SaveChangesAsync();
        await AutoVetoBotsAsync(db);
    }

    /// <summary>W.O. por não comparecer ao veto (Tournament.NoShowMinutes). MD3/MD5 escolhe TODOS
    /// os mapas da série de uma vez só no veto (ver AdvanceSeriesAsync em CompetitionEndpoints) —
    /// então um "sumiço" só pode acontecer aqui, antes do primeiro mapa: se o time da vez não agir
    /// dentro do prazo (contado desde a última ação, ou desde a abertura se ninguém agiu ainda), o
    /// adversário avança a chave por W.O. direto, sem precisar jogar nada.</summary>
    private static async Task CheckVetoNoShowsAsync(ApiDbContext db)
    {
        var now = DateTime.UtcNow;
        var pending = await db.VetoSessions.Include(s => s.Steps).Where(s => !s.IsComplete).ToListAsync();
        if (pending.Count == 0) return;

        foreach (var s in pending)
        {
            var bm = await db.BracketMatches.Include(m => m.Round).FirstOrDefaultAsync(m => m.Id == s.BracketMatchId);
            if (bm?.Round == null) continue;
            var t = await db.Tournaments.FindAsync(bm.Round.TournamentId);
            if (t == null) continue;

            var lastActivity = s.Steps.Count > 0 ? s.Steps.Max(x => x.CreatedAt) : s.CreatedAt;
            if (now < lastActivity.AddMinutes(t.NoShowMinutes)) continue;

            var seq = CompetitionEndpoints.BuildSequence(s.Series, s.MapPool.Count);
            if (s.StepIndex >= seq.Count) continue; // segurança — não deveria acontecer com IsComplete=false
            var (_, side) = seq[s.StepIndex];
            var noShowTag = side == 0 ? s.TeamATag : s.TeamBTag;
            var winnerTag = side == 0 ? s.TeamBTag : s.TeamATag;
            var winnerIsTeamA = winnerTag == s.TeamATag;

            s.IsComplete = true;
            await CompetitionEndpoints.Audit(db, "match_wo_noshow", null, null, null,
                bm.Round.TournamentId, null, noShowTag,
                $"{noShowTag} não fez o veto em {t.NoShowMinutes} min — W.O. pra {winnerTag}");

            foreach (var tag in new[] { s.TeamATag, s.TeamBTag })
            {
                var team = await db.Teams.FirstOrDefaultAsync(x => x.Tag == tag);
                if (team == null) continue;
                var tt = await db.TournamentTeams.FirstOrDefaultAsync(
                    x => x.TournamentId == bm.Round.TournamentId && x.TeamId == team.Id);
                var captainId = tt?.CaptainUserId ?? team.CaptainId;
                if (!string.IsNullOrEmpty(captainId))
                    await NotificationHelper.Notify(db, captainId, NotificationType.MatchNoShow,
                        $"{noShowTag} não apareceu pro veto — {winnerTag} avança por W.O.", bm.Round.TournamentId);
            }

            await CompetitionEndpoints.AdvanceBracketAsync(db, bm.Id, winnerIsTeamA);
        }
    }

    /// <summary>bd_loyal (docs/spec/summit-fase-final/plan.md RF-05) — não é disparado por
    /// resultado de partida, então é checado aqui, throttle simples pra não rodar a cada 20s o
    /// dia inteiro (GrantBadgeAsync já é idempotente, então rodar de mais só desperdiça query,
    /// nunca duplica badge).</summary>
    private static async Task CheckLoyaltyBadgesAsync(ApiDbContext db)
    {
        if (DateTime.UtcNow.Hour != 3) return;

        var now = DateTime.UtcNow;
        var eligible = await db.Users
            .Where(u => u.TeamId != null && u.TeamJoinedAt != null && now >= u.TeamJoinedAt!.Value.AddDays(365))
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var userId in eligible)
            await CompetitionEndpoints.GrantBadgeAsync(db, userId, "bd_loyal");
    }

    private static readonly HttpClient Http = new() { BaseAddress = new Uri("http://localhost:5180") };

    /// <summary>
    /// Bots do seed (capitães com id curto, ex. usr_ghost) jogam o veto sozinhos —
    /// um lance por tick, dando ritmo de adversário real. Dev only.
    /// </summary>
    private static async Task AutoVetoBotsAsync(ApiDbContext db)
    {
        var sessions = await db.VetoSessions.Include(s => s.Steps)
            .Where(s => !s.IsComplete).ToListAsync();
        foreach (var s in sessions)
        {
            var seq = CompetitionEndpoints.BuildSequence(s.Series, s.MapPool.Count);
            if (s.StepIndex >= seq.Count) continue;
            var (_, side) = seq[s.StepIndex];
            var tag = side == 0 ? s.TeamATag : s.TeamBTag;
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Tag == tag);
            if (team == null || team.CaptainId.Length > 12) continue;   // humano: não interfere
            var remaining = CompetitionEndpoints.RemainingMaps(s);
            if (remaining.Count == 0) continue;
            var map = remaining[Random.Shared.Next(remaining.Count)];
            try { await Http.PostAsJsonAsync($"/api/veto/{s.BracketMatchId}/action", new { teamTag = tag, map }); }
            catch { }
        }
    }

    /// <summary>Ponto de entrada único — despacha pro formato configurado (espec §6).
    /// internal (não private) só pra dar pro /api/debug/generate-bracket testar sem esperar
    /// os horários automáticos do T-30min/T-0.</summary>
    internal static async Task GenerateBracket(ApiDbContext db, Tournament t, List<TournamentTeam> teams)
    {
        if (t.FormatType == TournamentFormat.DoubleElimination)
            GenerateDoubleElimination(db, t, teams);
        else if (t.FormatType == TournamentFormat.Swiss)
            await GenerateSwissRoundAsync(db, t, teams, 0);
        else
            GenerateSingleElimination(db, t, teams, BracketSide.Upper);
    }

    /// <summary>Suíço (RF-07, plan.md): gera só a rodada seguinte, não a chave inteira — o
    /// pareamento da rodada N+1 depende dos resultados da rodada N (avanço de fato acontece em
    /// CompetitionEndpoints.AdvanceSwissAsync). Rodada 0 (chamada daqui) é sempre pareamento
    /// aleatório, já que ninguém tem campanha ainda. Devolve as partidas criadas pra quem chamou
    /// poder abrir o veto de cada uma.</summary>
    internal static async Task<List<BracketMatch>> GenerateSwissRoundAsync(
        ApiDbContext db, Tournament t, List<TournamentTeam> activeTeams, int roundIndex)
    {
        var (records, playedPairs, _) = await GetSwissHistoryAsync(db, t.Id);

        static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

        // agrupa por campanha (vitórias - derrotas), do melhor pro pior; embaralha dentro do grupo
        var pool = activeTeams
            .GroupBy(tt => records.TryGetValue(tt.Team!.Tag, out var r) ? r.Wins - r.Losses : 0)
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(_ => Random.Shared.Next()).ToList())
            .ToList();

        var pairs = new List<(TournamentTeam A, TournamentTeam B)>();
        for (int gi = 0; gi < pool.Count; gi++)
        {
            var group = pool[gi];
            while (group.Count > 0)
            {
                var a = group[0];
                group.RemoveAt(0);
                if (group.Count == 0)
                {
                    // sobrou ímpar: empresta alguém do grupo adjacente (mais próximo primeiro)
                    TournamentTeam? borrowed = null;
                    int borrowFrom = -1;
                    for (int step = 1; step < pool.Count && borrowed == null; step++)
                    {
                        foreach (var gj in new[] { gi + step, gi - step })
                        {
                            if (gj < 0 || gj >= pool.Count || pool[gj].Count == 0) continue;
                            borrowed = pool[gj].FirstOrDefault(x => !playedPairs.Contains(Key(a.Team!.Tag, x.Team!.Tag)))
                                       ?? pool[gj][0];
                            borrowFrom = gj;
                            break;
                        }
                    }
                    if (borrowed != null)
                    {
                        pool[borrowFrom].Remove(borrowed);
                        pairs.Add((a, borrowed));
                    }
                    break;
                }

                var idx = group.FindIndex(b => !playedPairs.Contains(Key(a.Team!.Tag, b.Team!.Tag)));
                if (idx == -1) idx = 0; // ninguém livre de rematch — força repetição, evita ficar sem par
                var opponent = group[idx];
                group.RemoveAt(idx);
                pairs.Add((a, opponent));
            }
        }

        var round = new BracketRound
        {
            Id = $"rnd_{Guid.NewGuid():N}",
            TournamentId = t.Id,
            RoundNumber = roundIndex + 1,
            Name = $"SUÍÇO — RODADA {roundIndex + 1}",
            Side = BracketSide.Upper
        };
        db.BracketRounds.Add(round);

        var newMatches = new List<BracketMatch>();
        int pos = 1;
        foreach (var (a, b) in pairs)
        {
            var bm = new BracketMatch
            {
                Id = $"bm_{Guid.NewGuid():N}",
                RoundId = round.Id,
                Position = pos++,
                TeamATag = a.Team!.Tag,
                TeamBTag = b.Team!.Tag,
                Status = BracketMatchStatus.Pending,
                ScheduledAt = DateTime.UtcNow.AddMinutes(5)
            };
            db.BracketMatches.Add(bm);
            newMatches.Add(bm);
        }
        return newMatches;
    }

    /// <summary>Campanha (vitórias/derrotas) de cada time e pares já enfrentados, derivados do
    /// histórico de `BracketMatch` já finalizadas (plan.md RF-07 — evita coluna nova no schema).
    /// Só olha partidas Upper porque o suíço nunca gera Lower/GrandFinal.</summary>
    internal static async Task<(Dictionary<string, (int Wins, int Losses)> Records, HashSet<string> PlayedPairs, List<BracketMatch> Finished)>
        GetSwissHistoryAsync(ApiDbContext db, string tournamentId)
    {
        var finished = await db.BracketMatches.Include(m => m.Round)
            .Where(m => m.Round!.TournamentId == tournamentId && m.Round.Side == BracketSide.Upper
                     && m.Status == BracketMatchStatus.Finished && m.ScoreA != null && m.ScoreB != null)
            .ToListAsync();

        var records = new Dictionary<string, (int Wins, int Losses)>();
        var pairs = new HashSet<string>();
        void Bump(string tag, bool win)
        {
            records.TryGetValue(tag, out var v);
            records[tag] = win ? (v.Wins + 1, v.Losses) : (v.Wins, v.Losses + 1);
        }
        foreach (var m in finished)
        {
            Bump(m.TeamATag, m.AWon);
            Bump(m.TeamBTag, m.BWon);
            pairs.Add(string.CompareOrdinal(m.TeamATag, m.TeamBTag) < 0
                ? $"{m.TeamATag}|{m.TeamBTag}" : $"{m.TeamBTag}|{m.TeamATag}");
        }
        return (records, pairs, finished);
    }

    /// <summary>Chave de eliminação simples — seed ALEATÓRIO (espec §5, sorteio pós check-in).
    /// Reaproveitada como a Upper bracket da eliminação dupla. Devolve as partidas agrupadas por
    /// rodada (posição = índice na lista) pra quem chamou poder fazer o roteamento de perdedor
    /// da eliminação dupla (ver GenerateDoubleElimination). Já preenche NextMatchId/NextMatchSlot
    /// (docs/spec/summit-fase-final/plan.md RF-02) e resolve BYE sozinho (avança direto, sem veto
    /// fantasma contra ninguém).</summary>
    private static List<List<BracketMatch>> GenerateSingleElimination(
        ApiDbContext db, Tournament t, List<TournamentTeam> teams, BracketSide side)
    {
        teams = teams.OrderBy(_ => Random.Shared.Next()).ToList();
        int n = teams.Count;
        int totalRounds = (int)Math.Ceiling(Math.Log2(Math.Max(n, 2)));

        static string RoundName(int index, int total) => (total - index) switch
        {
            1 => "FINAL",
            2 => "SEMIS",
            3 => "QUARTAS",
            _ => $"RODADA {index + 1}"
        };

        var roundMatches = new List<List<BracketMatch>>();
        for (int r = 0; r < totalRounds; r++)
        {
            var round = new BracketRound
            {
                Id = $"rnd_{Guid.NewGuid():N}",
                TournamentId = t.Id,
                RoundNumber = r + 1,
                Name = side == BracketSide.Upper ? RoundName(r, totalRounds) : $"UPPER {RoundName(r, totalRounds)}",
                Side = side
            };
            db.BracketRounds.Add(round);

            int matchesInRound = (int)Math.Ceiling(n / Math.Pow(2, r + 1));
            var thisRound = new List<BracketMatch>();
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
                thisRound.Add(bm);
            }

            // avanço "por vitória": partida p da rodada r-1 alimenta a partida p/2 da rodada r
            if (r > 0)
            {
                var prev = roundMatches[r - 1];
                for (int p = 0; p < prev.Count; p++)
                {
                    prev[p].NextMatchId = thisRound[p / 2].Id;
                    prev[p].NextMatchSlot = p % 2 == 0 ? 'A' : 'B';
                }
            }
            roundMatches.Add(thisRound);
        }

        // BYE na rodada 0 avança sozinho — não existe adversário real, não abre veto contra
        // ninguém. Limitação conhecida: não encadeia (um BYE puxando outro BYE em cascata).
        if (roundMatches.Count > 0)
        {
            foreach (var bm in roundMatches[0].Where(m => m.TeamBTag == "BYE"))
            {
                bm.Status = BracketMatchStatus.Finished;
                bm.ScoreA = 1;
                bm.ScoreB = 0;
                if (bm.NextMatchId != null)
                {
                    var next = roundMatches.SelectMany(rm => rm).FirstOrDefault(m => m.Id == bm.NextMatchId);
                    if (next != null)
                    {
                        if (bm.NextMatchSlot == 'A') next.TeamATag = bm.TeamATag;
                        else next.TeamBTag = bm.TeamATag;
                    }
                }
            }
        }

        return roundMatches;
    }

    /// <summary>
    /// Eliminação dupla (espec §6, docs/spec/summit-fase-final/plan.md RF-02): gera Upper +
    /// Lower + Grande Final já com o roteamento completo de vencedor E perdedor wireado
    /// (NextMatchId/LoserNextMatchId). Mapeamento verificado à mão contra N=4 e N=8 — ver
    /// plan.md pra tabela de referência. Limitação conhecida: BYE na Upper rodada 0 não
    /// propaga um "perdedor" pra Lower (não existe perdedor real numa partida de BYE).
    /// </summary>
    private static void GenerateDoubleElimination(ApiDbContext db, Tournament t, List<TournamentTeam> teams)
    {
        int n = teams.Count;
        int k = (int)Math.Ceiling(Math.Log2(Math.Max(n, 2)));

        var upperRounds = GenerateSingleElimination(db, t, teams, BracketSide.Upper);

        // Lower bracket: 2*(k-1) rodadas; k<2 (2 times) não tem lower que faça sentido.
        int lowerRounds = Math.Max(0, 2 * (k - 1));
        var lowerRoundMatches = new List<List<BracketMatch>>();
        for (int i = 0; i < lowerRounds; i++)
        {
            var round = new BracketRound
            {
                Id = $"rnd_{Guid.NewGuid():N}",
                TournamentId = t.Id,
                RoundNumber = 100 + i + 1, // namespace separado das rodadas da Upper
                Name = i == lowerRounds - 1 ? "LOWER FINAL" : $"LOWER {i + 1}",
                Side = BracketSide.Lower
            };
            db.BracketRounds.Add(round);

            int matchesInRound = Math.Max(1, (int)(n / Math.Pow(2, Math.Floor(i / 2.0) + 2)));
            var thisRound = new List<BracketMatch>();
            for (int p = 0; p < matchesInRound; p++)
            {
                var bm = new BracketMatch
                {
                    Id = $"bm_{Guid.NewGuid():N}",
                    RoundId = round.Id,
                    Position = p + 1,
                    TeamATag = "TBD",
                    TeamBTag = "TBD",
                    Status = BracketMatchStatus.Pending,
                    ScheduledAt = t.StartDate.AddHours(k + i)
                };
                db.BracketMatches.Add(bm);
                thisRound.Add(bm);
            }

            // avanço interno da Lower: ímpar = rodada de colocação (1:1, sobrevivente no slot A,
            // perdedor novo da Upper entra depois no slot B); par (>0) = eliminação (halving).
            if (i > 0)
            {
                var prev = lowerRoundMatches[i - 1];
                bool nextIsPlacement = i % 2 == 1;
                for (int p = 0; p < prev.Count; p++)
                {
                    if (nextIsPlacement)
                    {
                        prev[p].NextMatchId = thisRound[p].Id;
                        prev[p].NextMatchSlot = 'A';
                    }
                    else
                    {
                        prev[p].NextMatchId = thisRound[p / 2].Id;
                        prev[p].NextMatchSlot = p % 2 == 0 ? 'A' : 'B';
                    }
                }
            }
            lowerRoundMatches.Add(thisRound);
        }

        // roteamento do PERDEDOR da Upper pra Lower
        for (int r = 0; r < upperRounds.Count; r++)
        {
            var round = upperRounds[r];
            if (r == 0)
            {
                if (lowerRoundMatches.Count == 0) continue;
                var target = lowerRoundMatches[0];
                for (int p = 0; p < round.Count; p++)
                {
                    round[p].LoserNextMatchId = target[p / 2].Id;
                    round[p].LoserNextMatchSlot = p % 2 == 0 ? 'A' : 'B';
                }
            }
            else
            {
                int loserRoundIndex = 2 * r - 1;
                if (loserRoundIndex >= lowerRoundMatches.Count) continue;
                var target = lowerRoundMatches[loserRoundIndex];
                for (int p = 0; p < round.Count && p < target.Count; p++)
                {
                    round[p].LoserNextMatchId = target[p].Id;
                    round[p].LoserNextMatchSlot = 'B';
                }
            }
        }

        // Grande Final: Upper campeão (slot A) x Lower campeão (slot B). Partida de reset
        // (Tournament.BracketReset) é criada sob demanda em CompetitionEndpoints, não aqui.
        var grandFinalRound = new BracketRound
        {
            Id = $"rnd_{Guid.NewGuid():N}",
            TournamentId = t.Id,
            RoundNumber = 200,
            Name = "GRANDE FINAL",
            Side = BracketSide.GrandFinal
        };
        db.BracketRounds.Add(grandFinalRound);
        var grandFinal = new BracketMatch
        {
            Id = $"bm_{Guid.NewGuid():N}",
            RoundId = grandFinalRound.Id,
            Position = 1,
            TeamATag = "TBD",
            TeamBTag = "TBD",
            Status = BracketMatchStatus.Pending,
            ScheduledAt = t.StartDate.AddHours(k + lowerRounds)
        };
        db.BracketMatches.Add(grandFinal);

        if (upperRounds.Count > 0)
        {
            var upperFinal = upperRounds[^1][0];
            upperFinal.NextMatchId = grandFinal.Id;
            upperFinal.NextMatchSlot = 'A';
        }
        if (lowerRoundMatches.Count > 0)
        {
            var lowerFinal = lowerRoundMatches[^1][0];
            lowerFinal.NextMatchId = grandFinal.Id;
            lowerFinal.NextMatchSlot = 'B';
        }
    }
}
