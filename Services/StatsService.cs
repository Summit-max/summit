using Wallbang.Data;
using Wallbang.Models;

namespace Wallbang.Services;

public class StatsService : Interfaces.IStatsService
{
    private readonly UserRepository _userRepo = new();
    private readonly MatchRepository _matchRepo = new();

    public async Task<PlayerStats> GetStatsAsync(string userId)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        var matches = await _matchRepo.GetRecentForUserAsync(userId, 12);

        var recent = matches.Select(m =>
        {
            var mp = m.Players.FirstOrDefault(p => p.UserId == userId);
            var won = mp != null
                     && ((mp.TeamSide == "A" && m.TeamAWon) || (mp.TeamSide == "B" && m.TeamBWon));
            return new RecentPerformance
            {
                Date = m.PlayedAt,
                Map = m.Map,
                Won = won,
                Kills = mp?.Kills ?? 0,
                Deaths = mp?.Deaths ?? 0,
                Assists = mp?.Assists ?? 0,
                Score = m.Score
            };
        }).ToList();

        return new PlayerStats
        {
            UserId = userId,
            KD = user?.KD ?? 0,
            HeadshotPercent = user?.HeadshotPercent ?? 0,
            WinRate = user?.WinRate ?? 0,
            TotalMatches = user?.TotalMatches ?? 0,
            TotalWins = user?.TotalWins ?? 0,
            TotalKills = user?.TotalKills ?? 0,
            TotalDeaths = user?.TotalDeaths ?? 0,
            TotalAssists = user?.TotalAssists ?? 0,
            AvgDamagePerRound = user?.AvgDamagePerRound ?? 0,
            FavoriteMap = user?.FavoriteMap ?? "",
            FavoriteWeapon = user?.FavoriteWeapon ?? "",
            RecentPerformance = recent
        };
    }
}
