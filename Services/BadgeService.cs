using Wallbang.Data;
using Wallbang.Models;

namespace Wallbang.Services;

public class BadgeService : Interfaces.IBadgeService
{
    private readonly BadgeRepository _repo = new();

    public async Task<List<Badge>> GetAllForCurrentUserAsync()
    {
        var uid = App.UserService.CurrentUser?.Id;
        if (string.IsNullOrEmpty(uid)) return await _repo.GetAllAsync();
        return await _repo.GetAllWithStateForUserAsync(uid);
    }

    public Task<List<Badge>> GetUnlockedForUserAsync(string userId)
        => _repo.GetUnlockedForUserAsync(userId);
}
