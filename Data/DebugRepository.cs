using Summit.Services;

namespace Summit.Data;

/// <summary>
/// Chamadas pros endpoints /api/debug/* — ferramentas de teste local (docs/spec/summit-fase-final),
/// não fazem parte da superfície real de produto. Nunca usado fora de telas marcadas "DEV".
/// </summary>
public class DebugRepository
{
    public Task<bool> AddGhostTeamsAsync(string tournamentId, int count)
        => ApiClient.PostBoolAsync($"/api/debug/add-ghost-teams/{tournamentId}?count={count}");

    public Task<bool> SetTournamentDateAsync(string tournamentId, DateTime startDateUtc)
        => ApiClient.PostBoolAsync($"/api/debug/set-tournament-date/{tournamentId}", new { startDate = startDateUtc });

    public Task<bool> ForceCheckInAsync(string tournamentId, string teamId)
        => ApiClient.PostBoolAsync($"/api/debug/force-checkin/{tournamentId}/{teamId}");

    public Task<bool> SimulateVetoAsync(string bracketMatchId)
        => ApiClient.PostBoolAsync($"/api/debug/simulate-veto/{bracketMatchId}");

    public Task<bool> ForceMatchResultAsync(string bracketMatchId, char? winner)
        => ApiClient.PostBoolAsync($"/api/debug/force-match-result/{bracketMatchId}",
            winner.HasValue ? new { winner = winner.Value.ToString() } : null);
}
