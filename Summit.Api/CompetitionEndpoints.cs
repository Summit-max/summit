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

        app.MapPost("/api/teams/{teamId}/join-requests", async (ApiDbContext db, string teamId, JoinRequestBody body) =>
        {
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId);
            if (team == null || user == null) return Results.BadRequest("Time ou jogador inexistente.");
            if (user.TeamId != null) return Results.BadRequest("Jogador já pertence a um time.");

            var pending = await db.TeamJoinRequests.AnyAsync(r =>
                r.TeamId == teamId && r.UserId == body.UserId && r.Status == JoinRequestStatus.Pending);
            if (pending) return Results.BadRequest("Já existe solicitação pendente.");

            var req = new TeamJoinRequest
            {
                Id = $"jrq_{Guid.NewGuid():N}",
                TeamId = teamId,
                UserId = body.UserId,
                Message = body.Message ?? string.Empty
            };
            db.TeamJoinRequests.Add(req);
            await Audit(db, "join_request_created", body.UserId, null, teamId, null, null, null, null);
            await db.SaveChangesAsync();
            return Results.Ok(req);
        });

        // Só o dono vê as solicitações pendentes
        app.MapGet("/api/teams/{teamId}/join-requests", async (ApiDbContext db, string teamId, string ownerId) =>
        {
            if (!await IsOwner(db, teamId, ownerId)) return Results.Forbid();
            return Results.Ok(await db.TeamJoinRequests
                .Include(r => r.User)
                .Where(r => r.TeamId == teamId && r.Status == JoinRequestStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync());
        });

        app.MapPost("/api/teams/join-requests/{id}/accept", async (ApiDbContext db, string id, ActorBody body) =>
        {
            var req = await db.TeamJoinRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (req == null || req.Status != JoinRequestStatus.Pending) return Results.BadRequest();
            if (!await IsOwner(db, req.TeamId, body.ByUserId)) return Results.Forbid();

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

            await Audit(db, "join_request_accepted", body.ByUserId, req.UserId, req.TeamId, null, null, null, null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        app.MapPost("/api/teams/join-requests/{id}/decline", async (ApiDbContext db, string id, ActorBody body) =>
        {
            var req = await db.TeamJoinRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (req == null || req.Status != JoinRequestStatus.Pending) return Results.BadRequest();
            if (!await IsOwner(db, req.TeamId, body.ByUserId)) return Results.Forbid();
            req.Status = JoinRequestStatus.Declined;
            req.RespondedAt = DateTime.UtcNow;
            await Audit(db, "join_request_declined", body.ByUserId, req.UserId, req.TeamId, null, null, null, null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        app.MapPost("/api/teams/join-requests/{id}/cancel", async (ApiDbContext db, string id, ActorBody body) =>
        {
            var req = await db.TeamJoinRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (req == null || req.Status != JoinRequestStatus.Pending) return Results.BadRequest();
            if (req.UserId != body.ByUserId) return Results.Forbid();
            req.Status = JoinRequestStatus.Cancelled;
            req.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        // ═════════════ CARGOS: PROMOVER / REBAIXAR / TRANSFERIR (espec-times §10, §14) ═════════════

        app.MapPost("/api/teams/{teamId}/promote", async (ApiDbContext db, string teamId, RoleBody body) =>
        {
            if (!await IsOwner(db, teamId, body.ByUserId)) return Results.Forbid();
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
            if (target == null || target.TeamRole != TeamRole.Member) return Results.BadRequest();
            target.TeamRole = TeamRole.ViceCaptain;
            await Audit(db, "member_promoted", body.ByUserId, body.UserId, teamId, null, "Member", "ViceCaptain", null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        app.MapPost("/api/teams/{teamId}/demote", async (ApiDbContext db, string teamId, RoleBody body) =>
        {
            if (!await IsOwner(db, teamId, body.ByUserId)) return Results.Forbid();
            var target = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
            if (target == null || target.TeamRole != TeamRole.ViceCaptain) return Results.BadRequest();
            target.TeamRole = TeamRole.Member;
            await Audit(db, "member_demoted", body.ByUserId, body.UserId, teamId, null, "ViceCaptain", "Member", null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        app.MapPost("/api/teams/{teamId}/transfer-ownership", async (ApiDbContext db, string teamId, RoleBody body) =>
        {
            if (!await IsOwner(db, teamId, body.ByUserId)) return Results.Forbid();
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            var oldOwner = await db.Users.FirstOrDefaultAsync(u => u.Id == body.ByUserId);
            var newOwner = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
            if (team == null || oldOwner == null || newOwner == null) return Results.BadRequest();

            newOwner.TeamRole = TeamRole.Captain;
            oldOwner.TeamRole = TeamRole.ViceCaptain;   // antigo dono vira sublíder
            team.CaptainId = newOwner.Id;
            await Audit(db, "ownership_transferred", body.ByUserId, body.UserId, teamId, null, oldOwner.Nickname, newOwner.Nickname, null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        // ═════════════ CHECK-IN E ESCALAÇÃO (espec-campeonatos §4, espec-times §16-20) ═════════════

        app.MapPost("/api/tournaments/{id}/checkin", async (ApiDbContext db, string id, CheckInBody body) =>
        {
            var t = await db.Tournaments.FindAsync(id);
            if (t == null) return Results.BadRequest("Campeonato inexistente.");

            var now = DateTime.UtcNow;
            if (now < t.CheckInOpensAt) return Results.BadRequest("Check-in ainda não abriu (abre 1h antes do início).");
            if (now >= t.StartDate) return Results.BadRequest("Check-in encerrado.");

            var tt = await db.TournamentTeams.Include(x => x.Lineup)
                .FirstOrDefaultAsync(x => x.TournamentId == id && x.TeamId == body.TeamId);
            if (tt == null) return Results.BadRequest("Time não inscrito.");

            var by = await db.Users.FirstOrDefaultAsync(u => u.Id == body.ByUserId);
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
            await Audit(db, "checkin_confirmed", body.ByUserId, null, body.TeamId, id, null, null, null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        // Encerra a janela: remove quem não confirmou (espec-campeonatos §4)
        app.MapPost("/api/tournaments/{id}/close-checkin", async (ApiDbContext db, string id, ActorBody body) =>
        {
            var missing = await db.TournamentTeams
                .Where(x => x.TournamentId == id && x.CheckIn != CheckInStatus.Confirmed)
                .ToListAsync();
            foreach (var tt in missing)
            {
                tt.CheckIn = CheckInStatus.NoShow;
                tt.IsEliminated = true;
                await Audit(db, "team_noshow_removed", body.ByUserId, null, tt.TeamId, id, null, null, "Não realizou check-in");
            }
            await db.SaveChangesAsync();
            return Results.Ok(missing.Count);
        });

        // Alterar escalação — livre até a abertura do check-in (espec-times §18)
        app.MapPut("/api/tournaments/{id}/lineup", async (ApiDbContext db, string id, LineupBody body) =>
        {
            var t = await db.Tournaments.FindAsync(id);
            if (t == null) return Results.BadRequest("Campeonato inexistente.");
            if (DateTime.UtcNow >= t.CheckInOpensAt)
                return Results.BadRequest("Escalação bloqueada: o check-in já abriu.");

            var tt = await db.TournamentTeams.Include(x => x.Lineup)
                .FirstOrDefaultAsync(x => x.TournamentId == id && x.TeamId == body.TeamId);
            if (tt == null) return Results.BadRequest("Time não inscrito.");
            if (!await IsOwnerOrSub(db, body.TeamId, body.ByUserId)) return Results.Forbid();

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
            await Audit(db, "lineup_changed", body.ByUserId, null, body.TeamId, id, null,
                string.Join(",", body.PlayerIds), null);
            await db.SaveChangesAsync();
            return Results.Ok(true);
        });

        // ═════════════ SISTEMA DE VETOS (espec-campeonatos §8) ═════════════

        app.MapPost("/api/veto/{bracketMatchId}/start", async (ApiDbContext db, string bracketMatchId) =>
        {
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

        app.MapPost("/api/veto/{bracketMatchId}/action", async (ApiDbContext db, string bracketMatchId, VetoBody body) =>
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
                        ServerIp = $"sv{Random.Shared.Next(1, 9)}.summit.gg:{27015 + Random.Shared.Next(0, 4)}",
                        ServerPassword = $"smt_{Guid.NewGuid().ToString("N")[..8]}"
                    };
                    db.Matches.Add(room);
                    bm.MatchId = room.Id;
                }
                await Audit(db, "veto_completed", null, null, null, null, null,
                    string.Join(" | ", s.Steps.OrderBy(x => x.Order).Where(x => x.Action != VetoActionType.Ban).Select(x => x.Map)), null);
            }

            await db.SaveChangesAsync();
            var picks = s.Steps.Where(x => x.Action != VetoActionType.Ban).OrderBy(x => x.Order).Select(x => x.Map);
            return Results.Ok(new { complete = s.IsComplete, maps = picks, remaining = RemainingMaps(s) });
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
