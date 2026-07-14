using Summit.Models;

namespace Summit.Services.Interfaces;

public interface IBadgeService
{
    Task<List<Badge>> GetAllForCurrentUserAsync();
    Task<List<Badge>> GetUnlockedForUserAsync(string userId);
}
