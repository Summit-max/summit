using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class VetoRepository
{
    public Task<VetoState?> GetStateAsync(string bracketMatchId)
        => ApiClient.GetAsync<VetoState>($"/api/veto/{bracketMatchId}");

    public Task<VetoState?> StartAsync(string bracketMatchId)
        => ApiClient.PostAsync<VetoState>($"/api/veto/{bracketMatchId}/start", null);

    public Task<bool> ActAsync(string bracketMatchId, string teamTag, string map)
        => ApiClient.PostBoolAsync($"/api/veto/{bracketMatchId}/action", new { teamTag, map });

    public Task<Match?> GetRoomAsync(string bracketMatchId)
        => ApiClient.GetAsync<Match>($"/api/matches/by-bracket/{bracketMatchId}");
}
