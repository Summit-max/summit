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

    public Task<bool> CheckInAsync(string tournamentId, string teamId, string byUserId)
        => ApiClient.PostBoolAsync($"/api/tournaments/{tournamentId}/checkin", new { teamId, byUserId });

    public Task<(bool Ok, string? Message)> UpdateLineupAsync(string tournamentId, string teamId,
        string byUserId, List<string> playerIds, string? captainUserId)
        => ApiClient.PutWithMessageAsync($"/api/tournaments/{tournamentId}/lineup",
            new { teamId, byUserId, playerIds, captainUserId });

    public Task<(bool Ok, Tournament? Tournament, string? Message)> CreateTournamentAsync(
        string name, string? description, string? region, DateTime startDate,
        TournamentFormat formatType, SeriesFormat series, SeriesFormat finalSeries, string mapPoolCsv,
        int minTeams, int maxTeams, string? prize, bool isPaidEntry, string? entryFee,
        string organizerUserId, string? organizerName)
        => ApiClient.PostWithMessageAsync<Tournament>("/api/tournaments", new
        {
            name, description, region, startDate, formatType, series, finalSeries, mapPoolCsv,
            minTeams, maxTeams, prize, isPaidEntry, entryFee, organizerUserId, organizerName
        });

    public Task<(bool Ok, string? Message)> UpdateTournamentAsync(string tournamentId,
        string name, string? description, string? region, DateTime startDate,
        TournamentFormat formatType, SeriesFormat series, SeriesFormat finalSeries, string mapPoolCsv,
        int minTeams, int maxTeams, string? prize, bool isPaidEntry, string? entryFee, string byUserId)
        => ApiClient.PutWithMessageAsync($"/api/tournaments/{tournamentId}", new
        {
            name, description, region, startDate, formatType, series, finalSeries, mapPoolCsv,
            minTeams, maxTeams, prize, isPaidEntry, entryFee, byUserId
        });
}
