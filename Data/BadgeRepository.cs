using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class BadgeRepository
{
    public async Task<List<Badge>> GetAllAsync()
        => await ApiClient.GetAsync<List<Badge>>("/api/badges") ?? new();

    public async Task<List<Badge>> GetUnlockedForUserAsync(string userId)
        => await ApiClient.GetAsync<List<Badge>>($"/api/badges/user/{userId}") ?? new();

    public async Task<List<Badge>> GetAllWithStateForUserAsync(string userId)
        => await ApiClient.GetAsync<List<Badge>>($"/api/badges/user/{userId}/all") ?? new();
}
