using System.Net;
using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class TeamRepository
{
    public Task<Team?> GetByIdAsync(string teamId)
        => ApiClient.GetAsync<Team>($"/api/teams/{teamId}");

    public Task<Team?> GetByTagAsync(string tag)
        => ApiClient.GetAsync<Team>($"/api/teams/by-tag/{WebUtility.UrlEncode(tag)}");

    public async Task<List<Team>> GetAllAsync()
        => await ApiClient.GetAsync<List<Team>>("/api/teams") ?? new();

    public Task<Team> CreateAsync(string name, string tag, string captainId)
        => ApiClient.PostRequiredAsync<Team>("/api/teams", new { name, tag, captainId });

    public async Task<List<TeamInvitation>> GetInvitationsForUserAsync(string userId)
        => await ApiClient.GetAsync<List<TeamInvitation>>($"/api/teams/invitations/{userId}") ?? new();

    public Task<(bool Ok, TeamInvitation? Invitation, string? Message)> InviteAsync(
        string teamId, string invitedUserId, string invitedById)
        => ApiClient.PostWithMessageAsync<TeamInvitation>($"/api/teams/{teamId}/invite",
            new { invitedUserId, invitedById });

    public Task<bool> AcceptInvitationAsync(string invitationId)
        => ApiClient.PostBoolAsync($"/api/teams/invitations/{invitationId}/accept");

    public Task<bool> DeclineInvitationAsync(string invitationId)
        => ApiClient.PostBoolAsync($"/api/teams/invitations/{invitationId}/decline");

    public Task<bool> LeaveTeamAsync(string userId)
        => ApiClient.PostBoolAsync($"/api/teams/leave/{userId}");

    public async Task<List<TeamJoinRequest>> GetJoinRequestsAsync(string teamId, string ownerId)
        => await ApiClient.GetAsync<List<TeamJoinRequest>>($"/api/teams/{teamId}/join-requests?ownerId={ownerId}") ?? new();

    public Task<TeamJoinRequest?> CreateJoinRequestAsync(string teamId, string userId, string? message)
        => ApiClient.PostAsync<TeamJoinRequest>($"/api/teams/{teamId}/join-requests", new { userId, message });

    public Task<bool> AcceptJoinRequestAsync(string id, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/join-requests/{id}/accept", new { byUserId });

    public Task<bool> DeclineJoinRequestAsync(string id, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/join-requests/{id}/decline", new { byUserId });

    public Task<bool> PromoteAsync(string teamId, string userId, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/{teamId}/promote", new { userId, byUserId });

    public Task<bool> DemoteAsync(string teamId, string userId, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/{teamId}/demote", new { userId, byUserId });

    public Task<bool> TransferOwnershipAsync(string teamId, string userId, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/{teamId}/transfer-ownership", new { userId, byUserId });

    public Task<Team?> UpdateAsync(string teamId, string name, string? description, string? logoUrl, string? country, string byUserId)
        => ApiClient.PutAsync<Team>($"/api/teams/{teamId}", new { name, description, logoUrl, country, byUserId });

    public Task<(bool Ok, string? Message)> DeleteAsync(string teamId, string byUserId)
        => ApiClient.DeleteWithMessageAsync($"/api/teams/{teamId}?byUserId={byUserId}");

    public Task<bool> KickAsync(string teamId, string userId, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/{teamId}/kick", new { userId, byUserId });
}
