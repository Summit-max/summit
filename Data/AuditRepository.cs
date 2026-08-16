using Summit.Models;
using Summit.Services;

namespace Summit.Data;

public class AuditRepository
{
    public async Task<List<AuditLog>> GetAsync(string? teamId = null, string? tournamentId = null, int take = 50)
    {
        var qs = $"?take={take}"
            + (teamId != null ? $"&teamId={teamId}" : "")
            + (tournamentId != null ? $"&tournamentId={tournamentId}" : "");
        return await ApiClient.GetAsync<List<AuditLog>>($"/api/audit{qs}") ?? new();
    }
}
