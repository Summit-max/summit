using Wallbang.Models;

namespace Wallbang.Services.Interfaces;

public interface ITournamentService
{
    Task<List<Tournament>> GetTournamentsAsync();
    Task<Tournament?> GetTournamentAsync(string id);
    Task<bool> RegisterAsync(string tournamentId, string teamId);
}
