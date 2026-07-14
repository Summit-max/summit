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

    public Task<TeamInvitation?> InviteAsync(string teamId, string invitedUserId, string invitedById)
        => ApiClient.PostAsync<TeamInvitation>($"/api/teams/{teamId}/invite",
            new { invitedUserId, invitedById });

    public Task<bool> AcceptInvitationAsync(string invitationId)
        => ApiClient.PostBoolAsync($"/api/teams/invitations/{invitationId}/accept");

    public Task<bool> DeclineInvitationAsync(string invitationId)
        => ApiClient.PostBoolAsync($"/api/teams/invitations/{invitationId}/decline");

    public Task<bool> LeaveTeamAsync(string userId)
        => ApiClient.PostBoolAsync($"/api/teams/leave/{userId}");
}
