using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Summit.Api;
using Summit.Models;

var builder = WebApplication.CreateBuilder(args);

// ───── Banco: MySQL (env SUMMIT_DB ou appsettings), senão SQLite local (dev) ─────
var mysql = Environment.GetEnvironmentVariable("SUMMIT_DB")
         ?? builder.Configuration.GetConnectionString("MySql");

builder.Services.AddDbContext<ApiDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(mysql))
        o.UseMySql(mysql, ServerVersion.AutoDetect(mysql));
    else
        o.UseSqlite($"Data Source={Path.Combine(builder.Environment.ContentRootPath, "summit-api.db")}");
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var app = builder.Build();

// cria schema + seed de demonstração
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();
    await SeedData.EnsureSeededAsync(db);
}

app.MapGet("/", () => Results.Ok(new
{
    name = "Summit API",
    status = "ok",
    database = string.IsNullOrWhiteSpace(mysql) ? "sqlite (dev)" : "mysql",
}));

// ═════════════════════════════ USERS ═════════════════════════════

app.MapPost("/api/users/steam-login", async (ApiDbContext db, SteamLoginRequest req) =>
{
    var existing = await db.Users.Include(u => u.Team)
        .FirstOrDefaultAsync(u => u.SteamId == req.SteamId);

    if (existing == null)
    {
        var created = new User
        {
            Id = $"usr_{Guid.NewGuid():N}",
            SteamId = req.SteamId,
            Nickname = req.Nickname,
            AvatarUrl = req.AvatarUrl,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            Rank = "Unranked",
            PrimaryRole = "Rifler",
            Bio = string.Empty,
            FavoriteMap = string.Empty,
            TeamId = null,
            Level = 1
        };
        db.Users.Add(created);
        await db.SaveChangesAsync();
        return Results.Ok(created);
    }

    if (!string.IsNullOrWhiteSpace(req.Nickname))
        existing.Nickname = req.Nickname;
    if (!string.IsNullOrWhiteSpace(req.AvatarUrl))
        existing.AvatarUrl = req.AvatarUrl;
    existing.LastLoginAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

app.MapGet("/api/users/{id}", async (ApiDbContext db, string id) =>
{
    var u = await db.Users.Include(x => x.Team).ThenInclude(t => t!.Members)
        .FirstOrDefaultAsync(x => x.Id == id);
    return u == null ? Results.NotFound() : Results.Ok(u);
});

app.MapGet("/api/users/by-steam/{steamId}", async (ApiDbContext db, string steamId) =>
{
    var u = await db.Users.Include(x => x.Team).ThenInclude(t => t!.Members)
        .FirstOrDefaultAsync(x => x.SteamId == steamId);
    return u == null ? Results.NotFound() : Results.Ok(u);
});

app.MapGet("/api/users/by-nickname/{nickname}", async (ApiDbContext db, string nickname) =>
{
    var u = await db.Users
        .FirstOrDefaultAsync(x => x.Nickname.ToLower() == nickname.ToLower());
    return u == null ? Results.NotFound() : Results.Ok(u);
});

app.MapGet("/api/users/search", async (ApiDbContext db, string? q) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new List<User>());
    var query = q.ToLower();
    var list = await db.Users
        .Where(u => u.Nickname.ToLower().Contains(query))
        .OrderBy(u => u.Nickname)
        .Take(20)
        .ToListAsync();
    return Results.Ok(list);
});

app.MapPut("/api/users/{id}", async (ApiDbContext db, string id, User body) =>
{
    var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (existing == null) return Results.NotFound();

    existing.Nickname          = body.Nickname;
    existing.AvatarUrl         = body.AvatarUrl;
    existing.Bio               = body.Bio;
    existing.PrimaryRole       = body.PrimaryRole;
    existing.Rank              = body.Rank;
    existing.Level             = body.Level;
    existing.WinRate           = body.WinRate;
    existing.KD                = body.KD;
    existing.HeadshotPercent   = body.HeadshotPercent;
    existing.AvgDamagePerRound = body.AvgDamagePerRound;
    existing.TotalMatches      = body.TotalMatches;
    existing.TotalWins         = body.TotalWins;
    existing.TotalKills        = body.TotalKills;
    existing.TotalDeaths       = body.TotalDeaths;
    existing.TotalAssists      = body.TotalAssists;
    existing.Elo               = body.Elo;
    existing.FavoriteMap       = body.FavoriteMap;
    existing.FavoriteWeapon    = body.FavoriteWeapon;
    existing.Country           = body.Country;
    existing.TeamId            = body.TeamId;
    existing.TeamRole          = body.TeamRole;
    existing.TeamJoinedAt      = body.TeamJoinedAt;
    existing.LastLoginAt       = body.LastLoginAt;
    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

// ═════════════════════════════ TEAMS ═════════════════════════════

app.MapGet("/api/teams", async (ApiDbContext db) =>
    Results.Ok(await db.Teams.Include(t => t.Members).OrderByDescending(t => t.Elo).ToListAsync()));

app.MapGet("/api/teams/{id}", async (ApiDbContext db, string id) =>
{
    var t = await db.Teams.Include(x => x.Members).FirstOrDefaultAsync(x => x.Id == id);
    return t == null ? Results.NotFound() : Results.Ok(t);
});

app.MapGet("/api/teams/by-tag/{tag}", async (ApiDbContext db, string tag) =>
{
    var t = await db.Teams.Include(x => x.Members)
        .FirstOrDefaultAsync(x => x.Tag.ToLower() == tag.ToLower());
    return t == null ? Results.NotFound() : Results.Ok(t);
});

app.MapPost("/api/teams", async (ApiDbContext db, CreateTeamRequest req) =>
{
    var team = new Team
    {
        Id = $"team_{Guid.NewGuid():N}",
        Name = req.Name,
        Tag = req.Tag,
        CaptainId = req.CaptainId,
        CreatedAt = DateTime.UtcNow
    };
    db.Teams.Add(team);

    var captain = await db.Users.FirstOrDefaultAsync(u => u.Id == req.CaptainId);
    if (captain != null)
    {
        captain.TeamId = team.Id;
        captain.TeamRole = TeamRole.Captain;
        captain.TeamJoinedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return Results.Ok(team);
});

app.MapGet("/api/teams/invitations/{userId}", async (ApiDbContext db, string userId) =>
    Results.Ok(await db.TeamInvitations
        .Include(i => i.Team).ThenInclude(t => t!.Members)
        .Include(i => i.InvitedBy)
        .Where(i => i.InvitedUserId == userId && i.Status == TeamInvitationStatus.Pending)
        .OrderByDescending(i => i.CreatedAt)
        .ToListAsync()));

app.MapPost("/api/teams/{teamId}/invite", async (ApiDbContext db, string teamId, InviteRequest req) =>
{
    var inviter = await db.Users.FirstOrDefaultAsync(u => u.Id == req.InvitedById);
    if (inviter == null || inviter.TeamId != teamId) return Results.BadRequest();
    if (inviter.TeamRole != TeamRole.Captain && inviter.TeamRole != TeamRole.ViceCaptain)
        return Results.BadRequest();

    var target = await db.Users.FirstOrDefaultAsync(u => u.Id == req.InvitedUserId);
    if (target == null || target.TeamId != null) return Results.BadRequest();

    var existing = await db.TeamInvitations
        .FirstOrDefaultAsync(i => i.TeamId == teamId
                               && i.InvitedUserId == req.InvitedUserId
                               && i.Status == TeamInvitationStatus.Pending);
    if (existing != null) return Results.Ok(existing);

    var inv = new TeamInvitation
    {
        Id = $"inv_{Guid.NewGuid():N}",
        TeamId = teamId,
        InvitedUserId = req.InvitedUserId,
        InvitedById = req.InvitedById,
        Status = TeamInvitationStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };
    db.TeamInvitations.Add(inv);
    await db.SaveChangesAsync();
    return Results.Ok(inv);
});

app.MapPost("/api/teams/invitations/{id}/accept", async (ApiDbContext db, string id) =>
{
    var inv = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == id);
    if (inv == null || inv.Status != TeamInvitationStatus.Pending) return Results.BadRequest();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == inv.InvitedUserId);
    if (user == null || user.TeamId != null) return Results.BadRequest();

    user.TeamId = inv.TeamId;
    user.TeamRole = TeamRole.Member;
    user.TeamJoinedAt = DateTime.UtcNow;
    inv.Status = TeamInvitationStatus.Accepted;
    inv.RespondedAt = DateTime.UtcNow;

    var others = await db.TeamInvitations
        .Where(i => i.InvitedUserId == inv.InvitedUserId
                 && i.Status == TeamInvitationStatus.Pending
                 && i.Id != id)
        .ToListAsync();
    foreach (var o in others)
    {
        o.Status = TeamInvitationStatus.Cancelled;
        o.RespondedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/api/teams/invitations/{id}/decline", async (ApiDbContext db, string id) =>
{
    var inv = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == id);
    if (inv == null || inv.Status != TeamInvitationStatus.Pending) return Results.BadRequest();
    inv.Status = TeamInvitationStatus.Declined;
    inv.RespondedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapPost("/api/teams/leave/{userId}", async (ApiDbContext db, string userId) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null || user.TeamId == null) return Results.BadRequest();
    user.TeamId = null;
    user.TeamRole = TeamRole.Member;
    user.TeamJoinedAt = null;
    await db.SaveChangesAsync();
    return Results.Ok();
});

// ═════════════════════════════ TOURNAMENTS ═════════════════════════════

app.MapGet("/api/tournaments", async (ApiDbContext db) =>
    Results.Ok(await db.Tournaments
        .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
        .OrderBy(t => t.Status)
        .ThenBy(t => t.StartDate)
        .ToListAsync()));

app.MapGet("/api/tournaments/{id}", async (ApiDbContext db, string id) =>
{
    var t = await db.Tournaments
        .Include(x => x.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
        .Include(x => x.Bracket).ThenInclude(r => r.Matches)
        .FirstOrDefaultAsync(x => x.Id == id);
    return t == null ? Results.NotFound() : Results.Ok(t);
});

app.MapPost("/api/tournaments/{id}/register", async (ApiDbContext db, string id, RegisterTeamRequest req) =>
{
    var exists = await db.TournamentTeams
        .AnyAsync(x => x.TournamentId == id && x.TeamId == req.TeamId);
    if (exists) return Results.Ok(true);

    var t = await db.Tournaments.FindAsync(id);
    if (t == null) return Results.Ok(false);

    var count = await db.TournamentTeams.CountAsync(x => x.TournamentId == id);
    if (count >= t.MaxTeams) return Results.Ok(false);

    db.TournamentTeams.Add(new TournamentTeam
    {
        Id = $"tt_{Guid.NewGuid():N}",
        TournamentId = id,
        TeamId = req.TeamId,
        Seed = count + 1,
        RegisteredAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return Results.Ok(true);
});

app.MapGet("/api/tournaments/{id}/registered/{teamId}", async (ApiDbContext db, string id, string teamId) =>
    Results.Ok(await db.TournamentTeams.AnyAsync(x => x.TournamentId == id && x.TeamId == teamId)));

// ═════════════════════════════ MATCHES ═════════════════════════════

app.MapGet("/api/matches/recent", async (ApiDbContext db, string userId, int take) =>
    Results.Ok(await db.Matches
        .Include(m => m.Players)
        .Where(m => m.Players.Any(p => p.UserId == userId))
        .OrderByDescending(m => m.PlayedAt)
        .Take(take <= 0 ? 20 : take)
        .ToListAsync()));

app.MapGet("/api/matches/team/{teamId}", async (ApiDbContext db, string teamId, int take) =>
    Results.Ok(await db.Matches
        .Include(m => m.Players)
        .Where(m => m.TeamAId == teamId || m.TeamBId == teamId)
        .OrderByDescending(m => m.PlayedAt)
        .Take(take <= 0 ? 20 : take)
        .ToListAsync()));

app.MapGet("/api/matches/{id}", async (ApiDbContext db, string id) =>
{
    var m = await db.Matches
        .Include(x => x.Players).ThenInclude(p => p.User)
        .FirstOrDefaultAsync(x => x.Id == id);
    return m == null ? Results.NotFound() : Results.Ok(m);
});

// ═════════════════════════════ FRIENDS ═════════════════════════════

app.MapGet("/api/friends/{userId}", async (ApiDbContext db, string userId) =>
{
    var asRequester = db.Friendships
        .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Accepted)
        .Select(f => f.Addressee!);
    var asAddressee = db.Friendships
        .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Accepted)
        .Select(f => f.Requester!);
    return Results.Ok(await asRequester.Concat(asAddressee).OrderBy(u => u.Nickname).ToListAsync());
});

app.MapGet("/api/friends/{userId}/incoming", async (ApiDbContext db, string userId) =>
    Results.Ok(await db.Friendships
        .Include(f => f.Requester)
        .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
        .OrderByDescending(f => f.CreatedAt)
        .ToListAsync()));

app.MapGet("/api/friends/{userId}/outgoing", async (ApiDbContext db, string userId) =>
    Results.Ok(await db.Friendships
        .Include(f => f.Addressee)
        .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Pending)
        .OrderByDescending(f => f.CreatedAt)
        .ToListAsync()));

app.MapGet("/api/friends/relation", async (ApiDbContext db, string viewerId, string otherId) =>
{
    if (viewerId == otherId) return Results.Ok("None");
    var f = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == viewerId && x.AddresseeId == otherId) ||
        (x.RequesterId == otherId && x.AddresseeId == viewerId));
    if (f == null) return Results.Ok("None");
    if (f.Status == FriendshipStatus.Accepted) return Results.Ok("Friends");
    if (f.Status == FriendshipStatus.Pending)
        return Results.Ok(f.RequesterId == viewerId ? "OutgoingPending" : "IncomingPending");
    return Results.Ok("None");
});

app.MapPost("/api/friends/request", async (ApiDbContext db, FriendRequest req) =>
{
    if (req.RequesterId == req.AddresseeId) return Results.Ok(false);
    var existing = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == req.RequesterId && x.AddresseeId == req.AddresseeId) ||
        (x.RequesterId == req.AddresseeId && x.AddresseeId == req.RequesterId));
    if (existing != null) return Results.Ok(false);

    db.Friendships.Add(new Friendship
    {
        Id = $"fr_{Guid.NewGuid():N}",
        RequesterId = req.RequesterId,
        AddresseeId = req.AddresseeId,
        Status = FriendshipStatus.Pending,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return Results.Ok(true);
});

app.MapPost("/api/friends/{id}/accept", async (ApiDbContext db, string id, FriendActionRequest req) =>
{
    var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == id);
    if (f == null || f.AddresseeId != req.UserId || f.Status != FriendshipStatus.Pending)
        return Results.Ok(false);
    f.Status = FriendshipStatus.Accepted;
    f.RespondedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(true);
});

app.MapPost("/api/friends/{id}/decline", async (ApiDbContext db, string id, FriendActionRequest req) =>
{
    var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == id);
    if (f == null || f.AddresseeId != req.UserId || f.Status != FriendshipStatus.Pending)
        return Results.Ok(false);
    f.Status = FriendshipStatus.Declined;
    f.RespondedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(true);
});

app.MapDelete("/api/friends", async (ApiDbContext db, string userAId, string userBId) =>
{
    var f = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == userAId && x.AddresseeId == userBId) ||
        (x.RequesterId == userBId && x.AddresseeId == userAId));
    if (f == null) return Results.Ok(false);
    db.Friendships.Remove(f);
    await db.SaveChangesAsync();
    return Results.Ok(true);
});

// ═════════════════════════════ BADGES ═════════════════════════════

app.MapGet("/api/badges", async (ApiDbContext db) =>
    Results.Ok(await db.Badges.OrderBy(b => b.Name).ToListAsync()));

app.MapGet("/api/badges/user/{userId}", async (ApiDbContext db, string userId) =>
    Results.Ok(await (from ub in db.UserBadges
                      join b in db.Badges on ub.BadgeId equals b.Id
                      where ub.UserId == userId
                      orderby ub.UnlockedAt descending
                      select new Badge
                      {
                          Id = b.Id,
                          Name = b.Name,
                          Description = b.Description,
                          Icon = b.Icon,
                          Rarity = b.Rarity,
                          IsUnlocked = true,
                          UnlockedAt = ub.UnlockedAt
                      }).ToListAsync()));

app.MapGet("/api/badges/user/{userId}/all", async (ApiDbContext db, string userId) =>
{
    var unlocked = await db.UserBadges
        .Where(ub => ub.UserId == userId)
        .ToDictionaryAsync(ub => ub.BadgeId, ub => ub.UnlockedAt);

    var all = await db.Badges.OrderBy(b => b.Name).ToListAsync();
    foreach (var badge in all)
    {
        if (unlocked.TryGetValue(badge.Id, out var at))
        {
            badge.IsUnlocked = true;
            badge.UnlockedAt = at;
        }
    }
    return Results.Ok(all);
});

// ═════════════════════════════ RANKING ═════════════════════════════

app.MapGet("/api/ranking/players", async (ApiDbContext db) =>
{
    var users = await db.Users
        .Include(u => u.Team)
        .OrderByDescending(u => u.Elo)
        .Take(50)
        .ToListAsync();

    var list = users.Select((u, i) => new RankingPlayer
    {
        Position = i + 1,
        UserId = u.Id,
        Nickname = u.Nickname,
        AvatarUrl = u.AvatarUrl,
        Country = u.Country,
        TeamTag = u.Team?.Tag ?? "",
        Rank = u.Rank,
        Elo = u.Elo,
        Level = u.Level,
        WinRate = u.WinRate,
        KD = u.KD,
        Matches = u.TotalMatches
    }).ToList();
    return Results.Ok(list);
});

app.MapGet("/api/ranking/teams", async (ApiDbContext db) =>
{
    var teams = await db.Teams
        .Include(t => t.Members)
        .OrderByDescending(t => t.Elo)
        .Take(50)
        .ToListAsync();

    var list = teams.Select((t, i) => new RankingTeam
    {
        Position = i + 1,
        TeamId = t.Id,
        Name = t.Name,
        Tag = t.Tag,
        Country = t.Country,
        Elo = t.Elo,
        WinRate = t.WinRate,
        TournamentsWon = t.TournamentsWon,
        Matches = t.MatchesPlayed
    }).ToList();
    return Results.Ok(list);
});

app.Run("http://localhost:5180");

// ───── Request DTOs ─────
record SteamLoginRequest(string SteamId, string Nickname, string AvatarUrl);
record CreateTeamRequest(string Name, string Tag, string CaptainId);
record InviteRequest(string InvitedUserId, string InvitedById);
record RegisterTeamRequest(string TeamId);
record FriendRequest(string RequesterId, string AddresseeId);
record FriendActionRequest(string UserId);
