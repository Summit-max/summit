using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class MatchRepository
{
    public async Task<List<Match>> GetRecentForUserAsync(string userId, int take = 20)
        => await ApiClient.GetAsync<List<Match>>($"/api/matches/recent?userId={userId}&take={take}") ?? new();

    public async Task<List<Match>> GetRecentForTeamAsync(string teamId, int take = 20)
        => await ApiClient.GetAsync<List<Match>>($"/api/matches/team/{teamId}?take={take}") ?? new();

    public Task<Match?> GetByIdAsync(string matchId)
        => ApiClient.GetAsync<Match>($"/api/matches/{matchId}");

    public async Task<List<Match>> GetSeriesAsync(string bracketMatchId)
        => await ApiClient.GetAsync<List<Match>>($"/api/matches/by-bracket/{bracketMatchId}/all") ?? new();
}
