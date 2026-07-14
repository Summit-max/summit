using Wallbang.Models;

namespace Wallbang.Services.Interfaces;

public interface IStatsService
{
    Task<PlayerStats> GetStatsAsync(string userId);
}
