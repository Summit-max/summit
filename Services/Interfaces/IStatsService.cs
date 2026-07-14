using Summit.Models;

namespace Summit.Services.Interfaces;

public interface IStatsService
{
    Task<PlayerStats> GetStatsAsync(string userId);
}
