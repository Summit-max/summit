using Wallbang.Models;

namespace Wallbang.Services.Interfaces;

public interface IBadgeService
{
    Task<List<Badge>> GetAllForCurrentUserAsync();
    Task<List<Badge>> GetUnlockedForUserAsync(string userId);
}
