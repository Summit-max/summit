using System.Net;
using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class UserRepository
{
    public Task<User?> GetBySteamIdAsync(string steamId)
        => ApiClient.GetAsync<User>($"/api/users/by-steam/{WebUtility.UrlEncode(steamId)}");

    public Task<User> UpsertFromSteamAsync(string steamId, string nickname, string avatarUrl)
        => ApiClient.PostRequiredAsync<User>("/api/users/steam-login",
            new { steamId, nickname, avatarUrl });

    public async Task UpdateAsync(User user)
        => await ApiClient.PutAsync<User>($"/api/users/{user.Id}", user);

    public async Task<List<User>> SearchAsync(string query)
        => await ApiClient.GetAsync<List<User>>($"/api/users/search?q={WebUtility.UrlEncode(query)}") ?? new();

    public Task<User?> GetByIdAsync(string userId)
        => ApiClient.GetAsync<User>($"/api/users/{userId}");

    public Task<User?> GetByNicknameAsync(string nickname)
        => ApiClient.GetAsync<User>($"/api/users/by-nickname/{WebUtility.UrlEncode(nickname)}");
}
