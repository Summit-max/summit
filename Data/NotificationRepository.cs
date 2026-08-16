using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class NotificationRepository
{
    public async Task<List<Notification>> GetAsync(string userId, bool unreadOnly = false)
        => await ApiClient.GetAsync<List<Notification>>($"/api/notifications/{userId}?unreadOnly={unreadOnly}") ?? new();

    public Task<bool> MarkReadAsync(string id)
        => ApiClient.PostBoolAsync($"/api/notifications/{id}/read");

    public Task<bool> MarkAllReadAsync(string userId)
        => ApiClient.PostBoolAsync($"/api/notifications/{userId}/read-all");
}
