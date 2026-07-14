using Microsoft.EntityFrameworkCore;
using Wallbang.Models;

namespace Wallbang.Data;

public class MatchRepository
{
    public async Task<List<Match>> GetRecentForUserAsync(string userId, int take = 20)
    {
        using var db = new WallbangDbContext();
        return await db.Matches
            .Include(m => m.Players)
            .Where(m => m.Players.Any(p => p.UserId == userId))
            .OrderByDescending(m => m.PlayedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<List<Match>> GetRecentForTeamAsync(string teamId, int take = 20)
    {
        using var db = new WallbangDbContext();
        return await db.Matches
            .Include(m => m.Players)
            .Where(m => m.TeamAId == teamId || m.TeamBId == teamId)
            .OrderByDescending(m => m.PlayedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Match?> GetByIdAsync(string matchId)
    {
        using var db = new WallbangDbContext();
        return await db.Matches
            .Include(m => m.Players).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.Id == matchId);
    }
}
