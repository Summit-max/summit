using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Data;

public class UserRepository
{
    public async Task<User?> GetBySteamIdAsync(string steamId)
    {
        using var db = new SummitDbContext();
        return await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
    }

    public async Task<User> UpsertFromSteamAsync(string steamId, string nickname, string avatarUrl)
    {
        using var db = new SummitDbContext();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);

        if (existing == null)
        {
            var created = new User
            {
                Id = $"usr_{Guid.NewGuid():N}",
                SteamId = steamId,
                Nickname = nickname,
                AvatarUrl = avatarUrl,
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
            return created;
        }

        if (!string.IsNullOrWhiteSpace(nickname))
            existing.Nickname = nickname;
        if (!string.IsNullOrWhiteSpace(avatarUrl))
            existing.AvatarUrl = avatarUrl;
        existing.LastLoginAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task UpdateAsync(User user)
    {
        using var db = new SummitDbContext();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        if (existing == null) return;

        existing.Nickname          = user.Nickname;
        existing.AvatarUrl         = user.AvatarUrl;
        existing.Bio               = user.Bio;
        existing.PrimaryRole       = user.PrimaryRole;
        existing.Rank              = user.Rank;
        existing.Level             = user.Level;
        existing.WinRate           = user.WinRate;
        existing.KD                = user.KD;
        existing.HeadshotPercent   = user.HeadshotPercent;
        existing.AvgDamagePerRound = user.AvgDamagePerRound;
        existing.TotalMatches      = user.TotalMatches;
        existing.TotalWins         = user.TotalWins;
        existing.TotalKills        = user.TotalKills;
        existing.TotalDeaths       = user.TotalDeaths;
        existing.TotalAssists      = user.TotalAssists;
        existing.Elo               = user.Elo;
        existing.FavoriteMap       = user.FavoriteMap;
        existing.FavoriteWeapon    = user.FavoriteWeapon;
        existing.Country           = user.Country;
        existing.TeamId            = user.TeamId;
        existing.TeamRole          = user.TeamRole;
        existing.TeamJoinedAt      = user.TeamJoinedAt;
        existing.LastLoginAt       = user.LastLoginAt;
        await db.SaveChangesAsync();
    }

    public async Task<List<User>> SearchAsync(string query)
    {
        using var db = new SummitDbContext();
        if (string.IsNullOrWhiteSpace(query)) return new();
        var q = query.ToLower();
        return await db.Users
            .Where(u => u.Nickname.ToLower().Contains(q))
            .OrderBy(u => u.Nickname)
            .Take(20)
            .ToListAsync();
    }

    public async Task<User?> GetByIdAsync(string userId)
    {
        using var db = new SummitDbContext();
        return await db.Users
            .Include(u => u.Team)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<User?> GetByNicknameAsync(string nickname)
    {
        using var db = new SummitDbContext();
        return await db.Users
            .FirstOrDefaultAsync(u => u.Nickname.ToLower() == nickname.ToLower());
    }
}
