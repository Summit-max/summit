using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class FriendshipRepository
{
    public async Task<List<User>> GetFriendsAsync(string userId)
        => await ApiClient.GetAsync<List<User>>($"/api/friends/{userId}") ?? new();

    public async Task<List<Friendship>> GetIncomingRequestsAsync(string userId)
        => await ApiClient.GetAsync<List<Friendship>>($"/api/friends/{userId}/incoming") ?? new();

    public async Task<List<Friendship>> GetOutgoingRequestsAsync(string userId)
        => await ApiClient.GetAsync<List<Friendship>>($"/api/friends/{userId}/outgoing") ?? new();

    public enum RelationStatus { None, Friends, OutgoingPending, IncomingPending }

    public async Task<RelationStatus> GetRelationAsync(string viewerId, string otherId)
    {
        var raw = await ApiClient.GetAsync<string>(
            $"/api/friends/relation?viewerId={viewerId}&otherId={otherId}");
        return Enum.TryParse<RelationStatus>(raw, out var status) ? status : RelationStatus.None;
    }

    public Task<bool> SendRequestAsync(string requesterId, string addresseeId)
        => ApiClient.PostBoolAsync("/api/friends/request", new { requesterId, addresseeId });

    public Task<bool> AcceptRequestAsync(string friendshipId, string acceptorId)
        => ApiClient.PostBoolAsync($"/api/friends/{friendshipId}/accept", new { userId = acceptorId });

    public Task<bool> DeclineRequestAsync(string friendshipId, string declinerId)
        => ApiClient.PostBoolAsync($"/api/friends/{friendshipId}/decline", new { userId = declinerId });

    public Task<bool> RemoveFriendAsync(string userAId, string userBId)
        => ApiClient.DeleteBoolAsync($"/api/friends?userAId={userAId}&userBId={userBId}");
}
