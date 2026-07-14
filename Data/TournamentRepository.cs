using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class TournamentRepository
{
    public async Task<List<Tournament>> GetAllAsync()
        => await ApiClient.GetAsync<List<Tournament>>("/api/tournaments") ?? new();

    public Task<Tournament?> GetByIdAsync(string id)
        => ApiClient.GetAsync<Tournament>($"/api/tournaments/{id}");

    public Task<bool> RegisterTeamAsync(string tournamentId, string teamId)
        => ApiClient.PostBoolAsync($"/api/tournaments/{tournamentId}/register", new { teamId });

    public async Task<bool> IsTeamRegisteredAsync(string tournamentId, string teamId)
        => await ApiClient.GetAsync<bool>($"/api/tournaments/{tournamentId}/registered/{teamId}");
}
