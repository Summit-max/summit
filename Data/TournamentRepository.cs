using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Data;

public class TournamentRepository
{
    public async Task<List<Tournament>> GetAllAsync()
    {
        using var db = new SummitDbContext();
        return await db.Tournaments
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
            .OrderBy(t => t.Status)
            .ThenBy(t => t.StartDate)
            .ToListAsync();
    }

    public async Task<Tournament?> GetByIdAsync(string id)
    {
        using var db = new SummitDbContext();
        return await db.Tournaments
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
            .Include(t => t.Bracket).ThenInclude(r => r.Matches)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<bool> RegisterTeamAsync(string tournamentId, string teamId)
    {
        using var db = new SummitDbContext();
        var exists = await db.TournamentTeams
            .AnyAsync(x => x.TournamentId == tournamentId && x.TeamId == teamId);
        if (exists) return true;

        var t = await db.Tournaments.FindAsync(tournamentId);
        if (t == null) return false;

        var count = await db.TournamentTeams.CountAsync(x => x.TournamentId == tournamentId);
        if (count >= t.MaxTeams) return false;

        db.TournamentTeams.Add(new TournamentTeam
        {
            Id = $"tt_{Guid.NewGuid():N}",
            TournamentId = tournamentId,
            TeamId = teamId,
            Seed = count + 1,
            RegisteredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsTeamRegisteredAsync(string tournamentId, string teamId)
    {
        using var db = new SummitDbContext();
        return await db.TournamentTeams
            .AnyAsync(x => x.TournamentId == tournamentId && x.TeamId == teamId);
    }
}
