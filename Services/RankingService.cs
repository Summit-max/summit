using Summit.Models;

namespace Summit.Services;

public class RankingService
{
    public async Task<List<RankingPlayer>> GetTopPlayersAsync()
        => await ApiClient.GetAsync<List<RankingPlayer>>("/api/ranking/players") ?? new();

    public async Task<List<RankingTeam>> GetTopTeamsAsync()
        => await ApiClient.GetAsync<List<RankingTeam>>("/api/ranking/teams") ?? new();
}
