using Microsoft.EntityFrameworkCore;
using Summit.Data;
using Summit.Models;

namespace Summit.Services;

public class RankingService
{
    public async Task<List<RankingPlayer>> GetTopPlayersAsync()
    {
        using var db = new SummitDbContext();
        var users = await db.Users
            .Include(u => u.Team)
            .OrderByDescending(u => u.Elo)
            .Take(50)
            .ToListAsync();

        var list = new List<RankingPlayer>();
        for (int i = 0; i < users.Count; i++)
        {
            var u = users[i];
            list.Add(new RankingPlayer
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
            });
        }
        return list;
    }

    public async Task<List<RankingTeam>> GetTopTeamsAsync()
    {
        using var db = new SummitDbContext();
        var teams = await db.Teams
            .OrderByDescending(t => t.Elo)
            .Take(50)
            .ToListAsync();

        var list = new List<RankingTeam>();
        for (int i = 0; i < teams.Count; i++)
        {
            var t = teams[i];
            list.Add(new RankingTeam
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
            });
        }
        return list;
    }
}
