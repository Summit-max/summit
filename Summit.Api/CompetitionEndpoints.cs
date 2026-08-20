using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

/// <summary>
/// Endpoints das especificações funcionais (docs/espec-campeonatos.md e docs/espec-times.md).
/// Regra central de segurança (§43): toda permissão é validada aqui no backend.
/// </summary>
public static class CompetitionEndpoints
{
    public static void MapCompetitionEndpoints(this WebApplication app)
    {
        // ═════════════ SOLICITAÇÕES DE ENTRADA NO TIME (espec-times §8) ═════════════

        app.MapPost("/api/teams/{teamId}/join-requests", async (ApiDbContext db, HttpContext ctx, string teamId, JoinRequestBody body) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == authUserId);
            if (team == null || user == null) return Results.BadRequest("Time ou jogador inexistente.");
            if (user.TeamId != null) return Results.BadRequest("Jogador já pertence a um time.");

            var pending = await db.TeamJoinRequests.AnyAsync(r =>
                r.TeamId == teamId && r.UserId == authUserId && r.Status == JoinRequestStatus.Pending);
            if (pending) return Results.BadRequest("Já existe solicitação pendente.");

            var req = new TeamJoinRequest
            {
                Id = $"jrq_{Guid.NewGuid():N}",
                TeamId = teamId,
                UserId = authUserId,
                Message = body.Message ?? string.Empty
            };
            db.TeamJoinRequests.Add(req);
            await Audit(db, "join_request_created", authUserId, null, teamId, null, null, null, null);
            await db.SaveChangesAsync();
            return Results.Ok(req);
        }).RequireAuthorization();

        // Só o dono vê as solicitações pendentes
        app.MapGet("/api/teams/{teamId}/join-requests", async (ApiDbContext db, HttpContext ctx, string teamId) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            if (!await IsOwner(db, teamId, authUserId)) return Results.Forbid();
            return Results.Ok(await db.TeamJoinRequests
                .Include(r => r.User)
                .Where(r => r.TeamId == teamId && r.Status == JoinRequestStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync());
        }).RequireAuthorization();

        app.MapPost("/api/teams/join-requests/{id}/accept", async (ApiDbContext db, HttpContext ctx, string id) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var req = await db.TeamJoinRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (req == null || req.Status != JoinRequestStatus.Pending) return Results.BadRequest();
            if (!await IsOwner(db, req.TeamId, authUserId)) return Results.Forbid();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId);
            if (user == null || user.TeamId != null) return Results.BadRequest("Jogador não está mais elegível.");

            user.TeamId = req.TeamId;
            user.TeamRole = TeamRole.Member;
            user.TeamJoinedAt = DateTime.UtcNow;
            req.Status = JoinRequestStatus.Accepted;
            req.RespondedAt = DateTime.UtcNow;

            // cancela outras pendências do jogador
            var others = await db.TeamJoinRequests
                .Where(r => r.UserId == req.UserId && r.Status == JoinRequestStatus.Pending && r.Id != id)
                .ToListAsync();
            foreach (var o in others) { o.Status = JoinRequestStatus.Cancelled; o.RespondedAt = DateTime.UtcNow; }

            await Audit(db, "join_request_accepted", authUserId, req.UserId, req.TeamId, null, null, null, null);
            await NotificationHelper.Notify(db, req.UserId, NotificationType.JoinRequestResolved,
                "Sua solicitação de entrada foi aceita!", req.TeamId);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        app.MapPost("/api/teams/join-requests/{id}/decline", async (ApiDbContext db, HttpContext ctx, string id) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var req = await db.TeamJoinRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (req == null || req.Status != JoinRequestStatus.Pending) return Results.BadRequest();
            if (!await IsOwner(db, req.TeamId, authUserId)) return Results.Forbid();
            req.Status = JoinRequestStatus.Declined;
            req.RespondedAt = DateTime.UtcNow;
            await Audit(db, "join_request_declined", authUserId, req.UserId, req.TeamId, null, null, null, null);
            await NotificationHelper.Notify(db, req.UserId, NotificationType.JoinRequestResolved,
                "Sua solicitação de entrada foi recusada.", req.TeamId);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        app.MapPost("/api/teams/join-requests/{id}/cancel", async (ApiDbContext db, HttpContext ctx, string id) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var req = await db.TeamJoinRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (req == null || req.Status != JoinRequestStatus.Pending) return Results.BadRequest();
            if (req.UserId != authUserId) return Results.Forbid();
            req.Status = JoinRequestStatus.Cancelled;
            req.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        // ═════════════ CARGOS: PROMOVER / REBAIXAR / TRANSFERIR (espec-times §10, §14) ═════════════

        app.MapPost("/api/teams/{teamId}/promote", async (ApiDbContext db, HttpContext ctx, string teamId, RoleBody body) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            if (!await IsOwner(db, teamId, authUserId)) return Results.Forbid();
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
            if (target == null || target.TeamRole != TeamRole.Member) return Results.BadRequest();
            target.TeamRole = TeamRole.ViceCaptain;
            await Audit(db, "member_promoted", authUserId, body.UserId, teamId, null, "Member", "ViceCaptain", null);
            await NotificationHelper.Notify(db, body.UserId, NotificationType.RoleChanged, "Você foi promovido a sublíder.", teamId);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        app.MapPost("/api/teams/{teamId}/demote", async (ApiDbContext db, HttpContext ctx, string teamId, RoleBody body) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            if (!await IsOwner(db, teamId, authUserId)) return Results.Forbid();
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
            if (target == null || target.TeamRole != TeamRole.ViceCaptain) return Results.BadRequest();
            target.TeamRole = TeamRole.Member;
            await Audit(db, "member_demoted", authUserId, body.UserId, teamId, null, "ViceCaptain", "Member", null);
            await NotificationHelper.Notify(db, body.UserId, NotificationType.RoleChanged, "Você foi rebaixado a membro comum.", teamId);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        app.MapPost("/api/teams/{teamId}/transfer-ownership", async (ApiDbContext db, HttpContext ctx, string teamId, RoleBody body) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            if (!await IsOwner(db, teamId, authUserId)) return Results.Forbid();
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            var oldOwner = await db.Users.FirstOrDefaultAsync(u => u.Id == authUserId);
            var newOwner = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
            if (team == null || oldOwner == null || newOwner == null) return Results.BadRequest();

            newOwner.TeamRole = TeamRole.Captain;
            oldOwner.TeamRole = TeamRole.ViceCaptain;   // antigo dono vira sublíder
            team.CaptainId = newOwner.Id;
            await Audit(db, "ownership_transferred", authUserId, body.UserId, teamId, null, oldOwner.Nickname, newOwner.Nickname, null);
            await NotificationHelper.Notify(db, newOwner.Id, NotificationType.OwnershipTransferred,
                $"Você agora é o dono do time {team.Name}.", teamId);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        // ═════════════ CHECK-IN E ESCALAÇÃO (espec-campeonatos §4, espec-times §16-20) ═════════════

        app.MapPost("/api/tournaments/{id}/checkin", async (ApiDbContext db, HttpContext ctx, string id, CheckInBody body) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var t = await db.Tournaments.FindAsync(id);
            if (t == null) return Results.BadRequest("Campeonato inexistente.");

            var now = DateTime.UtcNow;
            if (now < t.CheckInOpensAt) return Results.BadRequest("Check-in ainda não abriu (abre 1h antes do início).");
            if (now >= t.StartDate) return Results.BadRequest("Check-in encerrado.");

            var tt = await db.TournamentTeams.Include(x => x.Lineup)
                .FirstOrDefaultAsync(x => x.TournamentId == id && x.TeamId == body.TeamId);
            if (tt == null) return Results.BadRequest("Time não inscrito.");

            var by = await db.Users.FirstOrDefaultAsync(u => u.Id == authUserId);
            var canCheckIn = by != null &&
                ((by.TeamId == body.TeamId && (by.TeamRole == TeamRole.Captain || by.TeamRole == TeamRole.ViceCaptain))
                 || by.Id == tt.CaptainUserId);
            if (!canCheckIn) return Results.Forbid();

            // revalida os 5 da escalação (§20)
            var lineupIds = tt.Lineup.Select(l => l.UserId).ToList();
            var stillValid = await db.Users.CountAsync(u => lineupIds.Contains(u.Id) && u.TeamId == body.TeamId);
            if (lineupIds.Count != 5 || stillValid != 5)
                return Results.BadRequest("Escalação inválida: o time precisa de 5 jogadores elegíveis.");

            tt.CheckIn = CheckInStatus.Confirmed;
            tt.CheckedInAt = now;
            await Audit(db, "checkin_confirmed", authUserId, null, body.TeamId, id, null, null, null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        // Encerra a janela: remove quem não confirmou (espec-campeonatos §4). Na prática o
        // LifecycleWorker já faz isso sozinho no T-30min direto via EF Core — este endpoint HTTP
        // não tinha NENHUMA checagem de quem podia chamar (achado no sweep de segurança); agora só
        // o organizador do campeonato pode acionar manualmente.
        app.MapPost("/api/tournaments/{id}/close-checkin", async (ApiDbContext db, HttpContext ctx, string id) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var t = await db.Tournaments.FindAsync(id);
            if (t == null) return Results.BadRequest("Campeonato inexistente.");
            if (t.OrganizerUserId != authUserId) return Results.Forbid();

            var missing = await db.TournamentTeams
                .Where(x => x.TournamentId == id && x.CheckIn != CheckInStatus.Confirmed)
                .ToListAsync();
            foreach (var tt in missing)
            {
                tt.CheckIn = CheckInStatus.NoShow;
                tt.IsEliminated = true;
                await Audit(db, "team_noshow_removed", authUserId, null, tt.TeamId, id, null, null, "Não realizou check-in");
            }
            await db.SaveChangesAsync();
            return Results.Ok(missing.Count);
        }).RequireAuthorization();

        // Alterar escalação — livre até a abertura do check-in (espec-times §18)
        app.MapPut("/api/tournaments/{id}/lineup", async (ApiDbContext db, HttpContext ctx, string id, LineupBody body) =>
        {
            var authUserId = SummitAuth.GetUserId(ctx)!;
            var t = await db.Tournaments.FindAsync(id);
            if (t == null) return Results.BadRequest("Campeonato inexistente.");
            if (DateTime.UtcNow >= t.CheckInOpensAt)
                return Results.BadRequest("Escalação bloqueada: o check-in já abriu.");

            var tt = await db.TournamentTeams.Include(x => x.Lineup)
                .FirstOrDefaultAsync(x => x.TournamentId == id && x.TeamId == body.TeamId);
            if (tt == null) return Results.BadRequest("Time não inscrito.");
            if (!await IsOwnerOrSub(db, body.TeamId, authUserId)) return Results.Forbid();

            var memberCount = await db.Users.CountAsync(u => u.TeamId == body.TeamId);
            var error = await ValidateLineupAsync(db, id, body.TeamId, body.PlayerIds, body.CaptainUserId,
                tt.Id, Math.Min(5, memberCount));
            if (error != null) return Results.BadRequest(error);

            db.TournamentLineupPlayers.RemoveRange(tt.Lineup);
            foreach (var pid in body.PlayerIds.Distinct())
                db.TournamentLineupPlayers.Add(new TournamentLineupPlayer
                {
                    Id = $"lp_{Guid.NewGuid():N}",
                    TournamentTeamId = tt.Id,
                    UserId = pid
                });
            tt.CaptainUserId = body.CaptainUserId;
            await Audit(db, "lineup_changed", authUserId, null, body.TeamId, id, null,
                string.Join(",", body.PlayerIds), null);
            foreach (var pid in body.PlayerIds.Distinct().Where(pid => pid != authUserId))
                await NotificationHelper.Notify(db, pid, NotificationType.LineupChanged,
                    "A escalação do seu time foi alterada.", id);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        }).RequireAuthorization();

        // ═════════════ SISTEMA DE VETOS (espec-campeonatos §8) ═════════════

        app.MapPost("/api/veto/{bracketMatchId}/start", async (ApiDbContext db, string bracketMatchId) =>
        {
            // idempotente e sem decisão privilegiada (só garante que a sessão existe) — fica
            // público de propósito porque é auto-chamado internamente (bots, debug) sem token de
            // usuário real. A proteção de verdade (quem pode dar cada lance) é no /action abaixo.
            var existing = await db.VetoSessions.Include(s => s.Steps)
                .FirstOrDefaultAsync(s => s.BracketMatchId == bracketMatchId);
            if (existing != null) return Results.Ok(existing);

            var bm = await db.BracketMatches.Include(m => m.Round)
                .FirstOrDefaultAsync(m => m.Id == bracketMatchId);
            if (bm == null) return Results.BadRequest("Partida inexistente.");

            var t = await db.Tournaments.FindAsync(bm.Round!.TournamentId);
            if (t == null) return Results.BadRequest();

            var isFinal = bm.Round.Name.Contains("FINAL", StringComparison.OrdinalIgnoreCase);
            var session = new VetoSession
            {
                Id = $"veto_{Guid.NewGuid():N}",
                BracketMatchId = bracketMatchId,
                Series = isFinal ? t.FinalSeries : t.Series,
                MapPoolCsv = t.MapPoolCsv,
                TeamATag = bm.TeamATag,
                TeamBTag = bm.TeamBTag
            };
            db.VetoSessions.Add(session);
            bm.Status = BracketMatchStatus.Veto;
            await db.SaveChangesAsync();
            return Results.Ok(session);
        });

        app.MapGet("/api/veto/{bracketMatchId}", async (ApiDbContext db, string bracketMatchId) =>
        {
            var s = await db.VetoSessions.Include(x => x.Steps.OrderBy(st => st.Order))
                .FirstOrDefaultAsync(x => x.BracketMatchId == bracketMatchId);
            if (s == null) return Results.NotFound();

            var remaining = RemainingMaps(s);
            var seq = BuildSequence(s.Series, s.MapPool.Count);
            object? next = null;
            if (!s.IsComplete && s.StepIndex < seq.Count)
            {
                var (action, side) = seq[s.StepIndex];
                next = new { action = action.ToString(), team = side == 0 ? s.TeamATag : s.TeamBTag };
            }
            return Results.Ok(new { session = s, remaining, next });
        });

        app.MapPost("/api/veto/{bracketMatchId}/action", async (ApiDbContext db, HttpContext ctx, IMatchServerProvider server, string bracketMatchId, VetoBody body) =>
        {
            var s = await db.VetoSessions.Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.BracketMatchId == bracketMatchId);
            if (s == null) return Results.BadRequest("Sessão de veto não iniciada.");
            if (s.IsComplete) return Results.BadRequest("Veto já concluído.");

            var seq = BuildSequence(s.Series, s.MapPool.Count);
            var (action, side) = seq[s.StepIndex];
            var expectedTag = side == 0 ? s.TeamATag : s.TeamBTag;
            if (!string.Equals(body.TeamTag, expectedTag, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest($"Não é a vez de {body.TeamTag} — vez de {expectedTag}.");

            // quando vem com token (cliente real de um humano), confere que quem está logado é
            // realmente do time que a vez pertence — sem isso um humano podia jogar pelo time
            // adversário só trocando o teamTag no corpo. Chamadas internas sem token (bots do
            // LifecycleWorker, /api/debug/simulate-veto) continuam funcionando sem essa checagem,
            // já que não representam um usuário real pra verificar.
            var authUserId = SummitAuth.GetUserId(ctx);
            if (authUserId != null)
            {
                var actingUser = await db.Users.Include(u => u.Team).FirstOrDefaultAsync(u => u.Id == authUserId);
                if (actingUser?.Team == null || !string.Equals(actingUser.Team.Tag, body.TeamTag, StringComparison.OrdinalIgnoreCase))
                    return Results.Forbid();
            }

            var remaining = RemainingMaps(s);
            var map = remaining.FirstOrDefault(m => string.Equals(m, body.Map, StringComparison.OrdinalIgnoreCase));
            if (map == null) return Results.BadRequest("Mapa indisponível (banido, já escolhido ou fora do pool).");

            s.Steps.Add(new VetoStep
            {
                Id = $"vst_{Guid.NewGuid():N}",
                SessionId = s.Id,
                Order = s.StepIndex,
                TeamTag = expectedTag,
                Action = action,
                Map = map
            });
            s.StepIndex++;

            // fim da sequência → mapa restante é o decider (§8)
            if (s.StepIndex >= seq.Count)
            {
                var last = RemainingMaps(s).First();
                s.Steps.Add(new VetoStep
                {
                    Id = $"vst_{Guid.NewGuid():N}",
                    SessionId = s.Id,
                    Order = s.StepIndex,
                    TeamTag = "—",
                    Action = VetoActionType.Decider,
                    Map = last
                });
                s.IsComplete = true;

                var bm = await db.BracketMatches.Include(m => m.Round)
                    .FirstOrDefaultAsync(m => m.Id == bracketMatchId);
                if (bm != null)
                {
                    bm.Status = BracketMatchStatus.PreparingServer;

                    // cria a SALA da partida: mapa definido + IP + senha (AWS na próxima fase)
                    var playMaps = s.Steps.Where(x => x.Action != VetoActionType.Ban)
                        .OrderBy(x => x.Order).Select(x => x.Map).ToList();
                    var teamA = await db.Teams.FirstOrDefaultAsync(x => x.Tag == s.TeamATag);
                    var teamB = await db.Teams.FirstOrDefaultAsync(x => x.Tag == s.TeamBTag);
                    var tour = bm.Round != null
                        ? await db.Tournaments.FindAsync(bm.Round.TournamentId) : null;
                    var room = new Match
                    {
                        Id = $"m_{Guid.NewGuid():N}",
                        Map = playMaps.First(),
                        GameNumber = 1,
                        PlayedAt = bm.ScheduledAt ?? DateTime.UtcNow,
                        Status = MatchStatus.Scheduled,
                        TeamAId = teamA?.Id ?? "",
                        TeamBId = teamB?.Id ?? "",
                        TeamATag = s.TeamATag,
                        TeamBTag = s.TeamBTag,
                        TeamAName = teamA?.Name ?? s.TeamATag,
                        TeamBName = teamB?.Name ?? s.TeamBTag,
                        TournamentId = tour?.Id,
                        TournamentName = tour?.Name,
                        BracketMatchId = bm.Id,
                        // quem decide o IP é o IMatchServerProvider (local simulado ou AWS real,
                        // docs/spec/summit-fase-final/plan.md RF-00) — nunca mais um atalho aqui.
                        ServerIp = "",
                        ServerPassword = $"smt_{Guid.NewGuid().ToString("N")[..8]}",
                        ProvisionState = ServerProvisionState.Requesting
                    };
                    db.Matches.Add(room);
                    bm.MatchId = room.Id;
                }
                await Audit(db, "veto_completed", null, null, null, null, null,
                    string.Join(" | ", s.Steps.OrderBy(x => x.Order).Where(x => x.Action != VetoActionType.Ban).Select(x => x.Map)), null);
            }

            await db.SaveChangesAsync();

            // veto acabou de fechar: tenta um servidor do pool quente primeiro (~segundos via RCON);
            // só cai pro cold boot de EC2 nova (~90s+) se o pool estiver sem servidor livre
            var newRoom = await db.Matches.Where(m => m.BracketMatchId == bracketMatchId)
                .OrderByDescending(m => m.GameNumber).FirstOrDefaultAsync();
            if (newRoom != null && newRoom.ProvisionState == ServerProvisionState.Requesting)
                await ProvisionRoomAsync(server, newRoom);

            var picks = s.Steps.Where(x => x.Action != VetoActionType.Ban).OrderBy(x => x.Order).Select(x => x.Map);
            return Results.Ok(new { complete = s.IsComplete, maps = picks, remaining = RemainingMaps(s) });
        });

        app.MapGet("/api/matches/by-bracket/{bracketMatchId}", async (ApiDbContext db, string bracketMatchId) =>
        {
            // numa série (MD3/MD5) pode haver mais de um Match pro mesmo confronto — um por mapa
            // já jogado; o "atual" é sempre o de maior GameNumber.
            var m = await db.Matches.Where(x => x.BracketMatchId == bracketMatchId)
                .OrderByDescending(x => x.GameNumber).FirstOrDefaultAsync();
            return m == null ? Results.NotFound() : Results.Ok(m);
        });

        // Histórico completo da série (todos os mapas jogados, um Match por GameNumber) —
        // usado pra exibir "Mapa 1: 16-4, Mapa 2: 8-16, ..." em MatchDetailsView.
        app.MapGet("/api/matches/by-bracket/{bracketMatchId}/all", async (ApiDbContext db, string bracketMatchId) =>
            Results.Ok(await db.Matches.Where(x => x.BracketMatchId == bracketMatchId)
                .OrderBy(x => x.GameNumber).ToListAsync()));

        // ═════════════ RESULTADO DE PARTIDA E AVANÇO DE CHAVE (fase pós-partida) ═════════════
        // docs/spec/summit-fase-final/spec.md RF-01/RF-02/RF-03. Único ponto de entrada de
        // resultado — tanto o LocalSimulatedMatchServerProvider quanto (no futuro) um webhook
        // real chamam este mesmo endpoint; nada rio abaixo sabe de onde o resultado veio.
        app.MapPost("/api/matches/{id}/result", async (ApiDbContext db, IMatchServerProvider server, string id, MatchResultBody body) =>
        {
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id);
            if (match == null) return Results.NotFound();
            if (match.Status == MatchStatus.Finished) return Results.Ok(true); // idempotente

            match.Status = MatchStatus.Finished;
            match.ScoreA = body.ScoreA;
            match.ScoreB = body.ScoreB;
            match.DurationMinutes = body.DurationMinutes;

            foreach (var p in body.PlayersA) db.MatchPlayers.Add(ToMatchPlayer(id, p, "A"));
            foreach (var p in body.PlayersB) db.MatchPlayers.Add(ToMatchPlayer(id, p, "B"));

            var aWon = body.ScoreA > body.ScoreB;

            await UpdateStatsAndEloAsync(db, match, body.PlayersA, body.PlayersB, aWon);
            await EvaluateBadgesAsync(db, body.PlayersA.Concat(body.PlayersB).Select(p => p.UserId));

            await Audit(db, "match_result_recorded", null, null, null, match.TournamentId,
                null, $"{body.ScoreA}-{body.ScoreB}", null);

            // MD3/MD5: esse mapa só decide a CHAVE se a série já estiver decidida — senão abre
            // o próximo mapa (já escolhido no veto original) sozinho, sem chave nova de veto.
            if (!string.IsNullOrEmpty(match.BracketMatchId))
                await AdvanceSeriesAsync(db, server, match.BracketMatchId!, match);

            // cada mapa provisiona um servidor NOVO e dedicado (não reaproveita entre mapas de
            // uma série) — sem isso, cada mapa jogado ficava uma EC2 ligada pra sempre, sem
            // nenhum jeito automático de derrubar (só servidor de pool tem esse ciclo, via
            // ReleaseEmptyAsync). Ec2InstanceId só vem preenchido no provisionamento direto —
            // servidor vindo do pool nunca seta esse campo, então não é afetado aqui.
            if (!string.IsNullOrEmpty(match.Ec2InstanceId))
                _ = server.TerminateAsync(match.Ec2InstanceId);

            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        // ═════════════ AUDITORIA ═════════════

        app.MapGet("/api/audit", async (ApiDbContext db, string? teamId, string? tournamentId, int take) =>
        {
            var q = db.AuditLogs.AsQueryable();
            if (!string.IsNullOrEmpty(teamId)) q = q.Where(a => a.TeamId == teamId);
            if (!string.IsNullOrEmpty(tournamentId)) q = q.Where(a => a.TournamentId == tournamentId);
            return Results.Ok(await q.OrderByDescending(a => a.CreatedAt).Take(take <= 0 ? 50 : take).ToListAsync());
        });
    }

    // ═════════════ Avanço de chave (docs/spec/summit-fase-final/plan.md RF-02/RF-03) ═════════════

    private static MatchPlayer ToMatchPlayer(string matchId, MatchPlayerResultBody p, string side) => new()
    {
        Id = $"mp_{Guid.NewGuid():N}",
        MatchId = matchId,
        UserId = p.UserId,
        TeamSide = side,
        Kills = p.Kills,
        Deaths = p.Deaths,
        Assists = p.Assists,
        HeadshotKills = p.HeadshotKills,
        AvgDamagePerRound = p.AvgDamagePerRound,
        Rating = p.Rating,
        IsMvp = p.IsMvp
    };

    /// <summary>MD1/MD3/MD5 (plan.md RF-02): o veto já escolheu TODOS os mapas da série de uma vez
    /// só (picks + decider) — aqui só sequenciamos por eles. Fecha o mapa que acabou de terminar
    /// e decide: falta mapa (ninguém alcançou a maioria dos mapas e ainda sobra mapa escolhido) →
    /// abre o próximo mapa sozinho, sem veto novo; série decidida → só aí chama
    /// <see cref="AdvanceBracketAsync"/>, com o vencedor sendo quem ganhou mais MAPAS na série,
    /// não quem ganhou o mapa que acabou de terminar.</summary>
    internal static async Task AdvanceSeriesAsync(ApiDbContext db, IMatchServerProvider server, string bracketMatchId, Match justFinished)
    {
        // flush antes de ler o histórico: "justFinished" (o mapa que acabou de fechar a série)
        // só existe como Finished no change tracker até aqui — a query abaixo roda SQL de
        // verdade (mesma pegadinha já vista no suíço: WHERE Status=Finished não enxerga uma
        // mudança ainda não salva), então sem isso o mapa que decidiu a série fica de fora da
        // contagem de vitórias.
        await db.SaveChangesAsync();

        var vetoSession = await db.VetoSessions.Include(v => v.Steps)
            .FirstOrDefaultAsync(v => v.BracketMatchId == bracketMatchId);
        var seriesMaps = vetoSession != null
            ? vetoSession.Steps.Where(x => x.Action != VetoActionType.Ban).OrderBy(x => x.Order).Select(x => x.Map).ToList()
            : new List<string> { justFinished.Map };

        var games = await db.Matches
            .Where(m => m.BracketMatchId == bracketMatchId && m.Status == MatchStatus.Finished)
            .ToListAsync();
        var winsA = games.Count(g => g.ScoreA > g.ScoreB);
        var winsB = games.Count(g => g.ScoreB > g.ScoreA);
        var neededWins = seriesMaps.Count / 2 + 1; // MD1→1, MD3→2, MD5→3

        if (winsA >= neededWins || winsB >= neededWins || justFinished.GameNumber >= seriesMaps.Count)
        {
            await AdvanceBracketAsync(db, bracketMatchId, winsA > winsB);
            return;
        }

        var nextGameNumber = justFinished.GameNumber + 1;
        var next = new Match
        {
            Id = $"m_{Guid.NewGuid():N}",
            Map = seriesMaps[nextGameNumber - 1],
            GameNumber = nextGameNumber,
            PlayedAt = DateTime.UtcNow,
            Status = MatchStatus.Scheduled,
            TeamAId = justFinished.TeamAId,
            TeamBId = justFinished.TeamBId,
            TeamATag = justFinished.TeamATag,
            TeamBTag = justFinished.TeamBTag,
            TeamAName = justFinished.TeamAName,
            TeamBName = justFinished.TeamBName,
            TournamentId = justFinished.TournamentId,
            TournamentName = justFinished.TournamentName,
            BracketMatchId = bracketMatchId,
            ServerIp = "",
            ServerPassword = $"smt_{Guid.NewGuid().ToString("N")[..8]}",
            ProvisionState = ServerProvisionState.Requesting
        };
        db.Matches.Add(next);

        var bm = await db.BracketMatches.FirstOrDefaultAsync(m => m.Id == bracketMatchId);
        if (bm != null)
        {
            bm.MatchId = next.Id;
            bm.Status = BracketMatchStatus.PreparingServer;
        }

        await db.SaveChangesAsync(); // o registro precisa existir antes do provider poder gravar o IP nele
        await ProvisionRoomAsync(server, next);
    }

    private static async Task ProvisionRoomAsync(IMatchServerProvider server, Match room)
    {
        var assignedFromPool = await server.TryAssignFromPoolAsync(room.Id, room.Map, room.ServerPassword);
        if (!assignedFromPool)
            _ = server.ProvisionAsync(room.Id);
    }

    internal static async Task AdvanceBracketAsync(ApiDbContext db, string bracketMatchId, bool aWon)
    {
        var bm = await db.BracketMatches.Include(m => m.Round).FirstOrDefaultAsync(m => m.Id == bracketMatchId);
        if (bm == null || bm.Round == null) return;

        var winnerTag = aWon ? bm.TeamATag : bm.TeamBTag;
        var loserTag = aWon ? bm.TeamBTag : bm.TeamATag;
        bm.Status = BracketMatchStatus.Finished;
        bm.ScoreA = aWon ? 1 : 0;
        bm.ScoreB = aWon ? 0 : 1;

        if (bm.Round.Side == BracketSide.Upper)
        {
            var t = await db.Tournaments.FindAsync(bm.Round.TournamentId);
            if (t != null && t.FormatType == TournamentFormat.Swiss)
            {
                await AdvanceSwissAsync(db, t, bm);
                return;
            }
        }

        if (bm.NextMatchId != null)
            await FillSlotAsync(db, bm.NextMatchId, bm.NextMatchSlot!.Value, winnerTag);

        if (bm.Round.Side == BracketSide.Upper)
        {
            if (bm.LoserNextMatchId != null)
            {
                await FillSlotAsync(db, bm.LoserNextMatchId, bm.LoserNextMatchSlot!.Value, loserTag);
            }
            else
            {
                await MarkEliminatedAsync(db, loserTag, bm.Round.TournamentId);
                if (bm.NextMatchId == null)
                {
                    // sem próxima rodada Upper nem Lower pra rotear = era a final (eliminação simples)
                    var t = await db.Tournaments.FindAsync(bm.Round.TournamentId);
                    if (t != null) await CloseTournamentAsync(db, t, winnerTag, loserTag);
                }
            }
        }
        else if (bm.Round.Side == BracketSide.Lower)
        {
            await MarkEliminatedAsync(db, loserTag, bm.Round.TournamentId); // perder na Lower = eliminado de vez
        }
        else // GrandFinal
        {
            await HandleGrandFinalResultAsync(db, bm, winnerTag, loserTag);
        }

        if (bm.NextMatchId != null) await OpenVetoForMatchIdAsync(db, bm.Round.TournamentId, bm.NextMatchId);
        if (bm.LoserNextMatchId != null) await OpenVetoForMatchIdAsync(db, bm.Round.TournamentId, bm.LoserNextMatchId);
    }

    private static async Task FillSlotAsync(ApiDbContext db, string matchId, char slot, string teamTag)
    {
        var target = await db.BracketMatches.FirstOrDefaultAsync(m => m.Id == matchId);
        if (target == null) return;
        if (slot == 'A') target.TeamATag = teamTag; else target.TeamBTag = teamTag;
    }

    private static async Task MarkEliminatedAsync(ApiDbContext db, string teamTag, string tournamentId)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Tag == teamTag);
        if (team == null) return;
        var tt = await db.TournamentTeams.FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.TeamId == team.Id);
        if (tt != null) tt.IsEliminated = true;
    }

    /// <summary>Suíço (plan.md RF-07): sem NextMatchId/roteamento de chave — cada rodada só
    /// avança pra próxima quando a ÚLTIMA partida pendente dela termina. Classifica/elimina por
    /// campanha (`SwissTargetWins`/`SwissEliminationLosses`) e encerra o campeonato quando não
    /// sobra ninguém ativo.</summary>
    private static async Task AdvanceSwissAsync(ApiDbContext db, Tournament t, BracketMatch bm)
    {
        var round = await db.BracketRounds.Include(r => r.Matches)
            .FirstOrDefaultAsync(r => r.Id == bm.RoundId);
        if (round == null) return;
        if (round.Matches.Any(m => m.Status != BracketMatchStatus.Finished)) return; // rodada ainda em andamento

        // flush antes de ler o histórico: bm (a partida que acabou de fechar a rodada) só existe
        // como Finished no change tracker até aqui — GetSwissHistoryAsync roda SQL de verdade
        // (filtro WHERE Status=Finished), que não enxerga uma mudança ainda não salva.
        await db.SaveChangesAsync();

        var (records, _, finishedMatches) = await LifecycleWorker.GetSwissHistoryAsync(db, t.Id);
        var allTeams = await db.TournamentTeams.Include(x => x.Team)
            .Where(x => x.TournamentId == t.Id).ToListAsync();

        var stillActive = new List<TournamentTeam>();
        int rankCounter = allTeams.Count(x => x.FinalPosition != null);
        foreach (var tt in allTeams)
        {
            if (tt.IsEliminated || tt.FinalPosition != null || tt.Team == null) continue;
            var (wins, losses) = records.TryGetValue(tt.Team.Tag, out var r) ? r : (0, 0);
            if (wins >= t.SwissTargetWins)
            {
                rankCounter++;
                tt.FinalPosition = rankCounter;
            }
            else if (losses >= t.SwissEliminationLosses)
            {
                tt.IsEliminated = true;
            }
            else
            {
                stillActive.Add(tt);
            }
        }

        if (stillActive.Count == 0)
        {
            var (championTag, runnerUpTag) = PickSwissFinalists(allTeams, records, finishedMatches);
            if (championTag != null) await CloseTournamentAsync(db, t, championTag, runnerUpTag ?? "—");
            return;
        }

        var newMatches = await LifecycleWorker.GenerateSwissRoundAsync(db, t, stillActive, round.RoundNumber);
        foreach (var m in newMatches) await OpenVetoForMatchAsync(db, t.Id, m);
    }

    /// <summary>Campeão suíço = classificado com melhor campanha (vitórias - derrotas), desempate
    /// por Buchholz (soma da campanha dos adversários enfrentados) — plan.md RF-07 §4.</summary>
    private static (string? Champion, string? RunnerUp) PickSwissFinalists(
        List<TournamentTeam> allTeams, Dictionary<string, (int Wins, int Losses)> records, List<BracketMatch> finishedMatches)
    {
        var classified = allTeams.Where(x => x.FinalPosition != null && x.Team != null).ToList();
        if (classified.Count == 0) return (null, null);

        int Campaign(string tag) => records.TryGetValue(tag, out var r) ? r.Wins - r.Losses : 0;
        int Buchholz(string tag) => finishedMatches
            .Where(m => m.TeamATag == tag || m.TeamBTag == tag)
            .Select(m => m.TeamATag == tag ? m.TeamBTag : m.TeamATag)
            .Sum(Campaign);

        var ordered = classified
            .OrderByDescending(x => Campaign(x.Team!.Tag))
            .ThenByDescending(x => Buchholz(x.Team!.Tag))
            .ToList();
        return (ordered[0].Team!.Tag, ordered.Count > 1 ? ordered[1].Team!.Tag : null);
    }

    /// <summary>Slot B é sempre o lado vindo da Lower na nossa geração (ver LifecycleWorker,
    /// GenerateDoubleElimination) — usado aqui só pra decidir se um reset de grande final se aplica.</summary>
    private static async Task HandleGrandFinalResultAsync(ApiDbContext db, BracketMatch bm, string winnerTag, string loserTag)
    {
        var t = await db.Tournaments.FindAsync(bm.Round!.TournamentId);
        if (t == null) return;

        var isFirstGame = bm.Round.RoundNumber == 200;
        var lowerSideWon = winnerTag == bm.TeamBTag;

        if (isFirstGame && t.BracketReset && lowerSideWon)
        {
            var resetRound = new BracketRound
            {
                Id = $"rnd_{Guid.NewGuid():N}",
                TournamentId = t.Id,
                RoundNumber = 201,
                Name = "GRANDE FINAL (RESET)",
                Side = BracketSide.GrandFinal
            };
            db.BracketRounds.Add(resetRound);
            db.BracketMatches.Add(new BracketMatch
            {
                Id = $"bm_{Guid.NewGuid():N}",
                RoundId = resetRound.Id,
                Position = 1,
                TeamATag = bm.TeamATag,
                TeamBTag = bm.TeamBTag,
                Status = BracketMatchStatus.Pending,
                ScheduledAt = DateTime.UtcNow.AddHours(1)
            });
            return; // torneio continua — reset decide de verdade
        }

        await CloseTournamentAsync(db, t, winnerTag, loserTag);
    }

    private static async Task CloseTournamentAsync(ApiDbContext db, Tournament t, string championTag, string runnerUpTag)
    {
        t.Status = TournamentStatus.Finished;
        var champion = await db.Teams.Include(x => x.Members).FirstOrDefaultAsync(x => x.Tag == championTag);
        var runnerUp = await db.Teams.FirstOrDefaultAsync(x => x.Tag == runnerUpTag);
        if (champion != null)
        {
            var ttChamp = await db.TournamentTeams.FirstOrDefaultAsync(x => x.TournamentId == t.Id && x.TeamId == champion.Id);
            if (ttChamp != null) ttChamp.FinalPosition = 1;
            champion.TournamentsWon++;
            foreach (var member in champion.Members) await GrantBadgeAsync(db, member.Id, "bd_champion");
        }
        if (runnerUp != null)
        {
            var ttRunner = await db.TournamentTeams.FirstOrDefaultAsync(x => x.TournamentId == t.Id && x.TeamId == runnerUp.Id);
            if (ttRunner != null) ttRunner.FinalPosition = 2;
        }
        await Audit(db, "tournament_finished", null, null, null, t.Id, null, championTag, null);

        var participants = await db.TournamentTeams.Include(x => x.Team)
            .Where(x => x.TournamentId == t.Id).ToListAsync();
        foreach (var tt in participants)
        {
            var captainId = tt.CaptainUserId ?? tt.Team?.CaptainId;
            if (!string.IsNullOrEmpty(captainId))
                await NotificationHelper.Notify(db, captainId, NotificationType.TournamentFinished,
                    $"{t.Name} foi encerrado — campeão: {championTag}.", t.Id);
        }
    }

    private static async Task OpenVetoForMatchIdAsync(ApiDbContext db, string tournamentId, string bracketMatchId)
    {
        var bm = await db.BracketMatches.FirstOrDefaultAsync(m => m.Id == bracketMatchId);
        await OpenVetoForMatchAsync(db, tournamentId, bm);
    }

    /// <summary>Abre veto pra uma partida se os dois lados já estão preenchidos — reusado tanto
    /// pelo LifecycleWorker (rodada 1, na abertura do campeonato) quanto por AdvanceBracketAsync
    /// (qualquer rodada seguinte, assim que fica completa).</summary>
    public static async Task OpenVetoForMatchAsync(ApiDbContext db, string? tournamentId, BracketMatch? bm)
    {
        if (bm == null || tournamentId == null) return;
        if (bm.TeamATag == "TBD" || bm.TeamBTag == "TBD") return;
        if (bm.TeamATag == "BYE" || bm.TeamBTag == "BYE") return;
        if (bm.Status != BracketMatchStatus.Pending) return;
        var hasVeto = await db.VetoSessions.AnyAsync(v => v.BracketMatchId == bm.Id);
        if (hasVeto) return;

        var t = await db.Tournaments.FindAsync(tournamentId);
        if (t == null) return;
        var round = await db.BracketRounds.FindAsync(bm.RoundId);
        var isFinal = round != null && round.Name.Contains("FINAL", StringComparison.OrdinalIgnoreCase);

        db.VetoSessions.Add(new VetoSession
        {
            Id = $"veto_{Guid.NewGuid():N}",
            BracketMatchId = bm.Id,
            Series = isFinal ? t.FinalSeries : t.Series,
            MapPoolCsv = t.MapPoolCsv,
            TeamATag = bm.TeamATag,
            TeamBTag = bm.TeamBTag
        });
        bm.Status = BracketMatchStatus.Veto;
    }

    // ═════════════ Estatísticas e Elo (plan.md RF-04) ═════════════

    private static async Task UpdateStatsAndEloAsync(ApiDbContext db, Match match,
        List<MatchPlayerResultBody> playersA, List<MatchPlayerResultBody> playersB, bool aWon)
    {
        var teamA = await db.Teams.FirstOrDefaultAsync(t => t.Id == match.TeamAId);
        var teamB = await db.Teams.FirstOrDefaultAsync(t => t.Id == match.TeamBId);
        var deltaA = 0;
        var deltaB = 0;
        if (teamA != null && teamB != null)
        {
            deltaA = ComputeEloDelta(teamA.Elo, teamB.Elo, aWon);
            deltaB = -deltaA;
            teamA.Elo += deltaA;
            teamB.Elo += deltaB;
            teamA.MatchesPlayed++;
            teamB.MatchesPlayed++;
            if (aWon) teamA.MatchesWon++; else teamB.MatchesWon++;
        }

        foreach (var p in playersA) await ApplyPlayerStatsAsync(db, p, aWon, deltaA);
        foreach (var p in playersB) await ApplyPlayerStatsAsync(db, p, !aWon, deltaB);
    }

    /// <summary>Elo padrão K=32, deliberadamente simples — cada jogador do time recebe o mesmo
    /// delta do time (não isola performance individual). Ver plan.md RF-04.</summary>
    private static int ComputeEloDelta(int eloA, int eloB, bool aWon)
    {
        const int K = 32;
        var expectedA = 1.0 / (1.0 + Math.Pow(10, (eloB - eloA) / 400.0));
        var scoreA = aWon ? 1.0 : 0.0;
        return (int)Math.Round(K * (scoreA - expectedA));
    }

    private static async Task ApplyPlayerStatsAsync(ApiDbContext db, MatchPlayerResultBody p, bool won, int eloDelta)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == p.UserId);
        if (user == null) return;

        user.TotalMatches++;
        if (won) user.TotalWins++;
        user.TotalKills += p.Kills;
        user.TotalDeaths += p.Deaths;
        user.TotalAssists += p.Assists;
        user.Elo += eloDelta;
        user.KD = user.TotalDeaths == 0 ? user.TotalKills : Math.Round((double)user.TotalKills / user.TotalDeaths, 2);
        user.WinRate = user.TotalMatches == 0 ? 0 : Math.Round((double)user.TotalWins / user.TotalMatches, 2);

        var hsThisMatch = p.Kills == 0 ? 0 : (double)p.HeadshotKills / p.Kills;
        user.HeadshotPercent = Math.Round(((user.HeadshotPercent * (user.TotalMatches - 1)) + hsThisMatch) / user.TotalMatches, 2);
        user.AvgDamagePerRound = Math.Round(((user.AvgDamagePerRound * (user.TotalMatches - 1)) + p.AvgDamagePerRound) / user.TotalMatches, 1);
    }

    // ═════════════ Badges automáticas (plan.md RF-05) ═════════════

    private static async Task EvaluateBadgesAsync(ApiDbContext db, IEnumerable<string> userIds)
    {
        foreach (var userId in userIds.Distinct())
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) continue;

            if (user.TotalWins == 1) await GrantBadgeAsync(db, userId, "bd_firstwin");

            var mvpCount = await db.MatchPlayers.CountAsync(mp => mp.UserId == userId && mp.IsMvp);
            if (mvpCount >= 10) await GrantBadgeAsync(db, userId, "bd_mvp");

            var last30 = await db.MatchPlayers.Where(mp => mp.UserId == userId)
                .OrderByDescending(mp => mp.Match!.PlayedAt).Take(30).ToListAsync();
            if (last30.Count >= 30)
            {
                var avgHs = last30.Average(mp => mp.Kills == 0 ? 0 : (double)mp.HeadshotKills / mp.Kills);
                if (avgHs >= 0.50) await GrantBadgeAsync(db, userId, "bd_hunter");
            }
        }
    }

    public static async Task GrantBadgeAsync(ApiDbContext db, string userId, string badgeId)
    {
        var already = await db.UserBadges.AnyAsync(ub => ub.UserId == userId && ub.BadgeId == badgeId);
        if (already) return;
        db.UserBadges.Add(new UserBadge
        {
            Id = $"ub_{Guid.NewGuid():N}",
            UserId = userId,
            BadgeId = badgeId,
            UnlockedAt = DateTime.UtcNow
        });
        var badge = await db.Badges.FirstOrDefaultAsync(b => b.Id == badgeId);
        await NotificationHelper.Notify(db, userId, NotificationType.BadgeUnlocked,
            $"Você desbloqueou a badge \"{badge?.Name ?? badgeId}\"!", badgeId);
    }

    // ═════════════ Helpers ═════════════

    public static async Task<bool> IsOwner(ApiDbContext db, string teamId, string userId)
        => await db.Users.AnyAsync(u => u.Id == userId && u.TeamId == teamId && u.TeamRole == TeamRole.Captain);

    public static async Task<bool> IsOwnerOrSub(ApiDbContext db, string teamId, string userId)
        => await db.Users.AnyAsync(u => u.Id == userId && u.TeamId == teamId &&
            (u.TeamRole == TeamRole.Captain || u.TeamRole == TeamRole.ViceCaptain));

    /// <summary>
    /// Valida a escalação + capitão (espec-times §16). O padrão competitivo é 5;
    /// enquanto o time tem elenco menor, aceita o elenco completo (modo alpha).
    /// </summary>
    public static async Task<string?> ValidateLineupAsync(
        ApiDbContext db, string tournamentId, string teamId,
        List<string> playerIds, string? captainUserId, string? ignoreTournamentTeamId,
        int requiredCount = 5)
    {
        var ids = playerIds.Distinct().ToList();
        if (ids.Count != requiredCount) return $"A escalação precisa de exatamente {requiredCount} jogadores.";

        var inTeam = await db.Users.CountAsync(u => ids.Contains(u.Id) && u.TeamId == teamId);
        if (inTeam != requiredCount) return "Todos os jogadores da escalação precisam pertencer ao time.";

        if (string.IsNullOrEmpty(captainUserId) || !ids.Contains(captainUserId))
            return "O capitão da escalação deve estar entre os 5 selecionados.";

        // nenhum jogador pode representar outro time no mesmo campeonato (§9)
        var conflict = await db.TournamentLineupPlayers
            .Include(lp => lp.TournamentTeam)
            .AnyAsync(lp => ids.Contains(lp.UserId)
                         && lp.TournamentTeam!.TournamentId == tournamentId
                         && lp.TournamentTeamId != ignoreTournamentTeamId);
        if (conflict) return "Um dos jogadores já está inscrito por outro time neste campeonato.";

        return null;
    }

    /// <summary>
    /// Sequência de vetos (espec-campeonatos §8): 2 bans → picks (0/2/4 conforme série)
    /// → bans alternados até restar 1 mapa (decider). Lado 0 = Time A, 1 = Time B.
    /// </summary>
    public static List<(VetoActionType action, int side)> BuildSequence(SeriesFormat series, int poolSize)
    {
        int picks = series switch
        {
            SeriesFormat.MD3 => 2,
            SeriesFormat.MD5 => 4,
            _ => 0
        };
        var steps = new List<(VetoActionType, int)>();
        int totalSteps = Math.Max(poolSize - 1, 0);
        int bansBefore = Math.Min(2, Math.Max(totalSteps - picks, 0));
        int bansAfter = Math.Max(totalSteps - picks - bansBefore, 0);

        int i = 0;
        for (int k = 0; k < bansBefore; k++) steps.Add((VetoActionType.Ban, i++ % 2));
        for (int k = 0; k < picks; k++) steps.Add((VetoActionType.Pick, i++ % 2));
        for (int k = 0; k < bansAfter; k++) steps.Add((VetoActionType.Ban, i++ % 2));
        return steps;
    }

    public static List<string> RemainingMaps(VetoSession s)
    {
        var used = s.Steps.Select(st => st.Map).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return s.MapPool.Where(m => !used.Contains(m)).ToList();
    }

    public static Task Audit(ApiDbContext db, string action, string? actor, string? target,
        string? teamId, string? tournamentId, string? oldValue, string? newValue, string? reason)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = $"aud_{Guid.NewGuid():N}",
            Action = action,
            ActorUserId = actor,
            TargetUserId = target,
            TeamId = teamId,
            TournamentId = tournamentId,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = reason
        });
        return Task.CompletedTask;
    }
}

// ───── Bodies ─────
public record JoinRequestBody(string UserId, string? Message);
public record ActorBody(string ByUserId);
public record RoleBody(string UserId, string ByUserId);
public record CheckInBody(string TeamId, string ByUserId);
public record LineupBody(string TeamId, string ByUserId, List<string> PlayerIds, string? CaptainUserId);
public record VetoBody(string TeamTag, string Map);
public record MatchPlayerResultBody(string UserId, int Kills, int Deaths, int Assists,
    int HeadshotKills, double AvgDamagePerRound, double Rating, bool IsMvp);
public record MatchResultBody(int ScoreA, int ScoreB, int DurationMinutes,
    List<MatchPlayerResultBody> PlayersA, List<MatchPlayerResultBody> PlayersB);
