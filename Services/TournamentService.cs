using Summit.Data;
using Summit.Models;

namespace Summit.Services;

public class TournamentService : Interfaces.ITournamentService
{
    private readonly TournamentRepository _repo = new();

    public async Task<List<Tournament>> GetTournamentsAsync()
    {
        var list = await _repo.GetAllAsync();
        await MarkRegisteredAsync(list);
        return list;
    }

    public async Task<Tournament?> GetTournamentAsync(string id)
    {
        var t = await _repo.GetByIdAsync(id);
        if (t != null) await MarkRegisteredAsync(new List<Tournament> { t });
        return t;
    }

    public Task<bool> RegisterAsync(string tournamentId, string teamId)
        => _repo.RegisterTeamAsync(tournamentId, teamId);

    private async Task MarkRegisteredAsync(List<Tournament> list)
    {
        var teamId = App.UserService.CurrentUser?.TeamId;
        if (string.IsNullOrEmpty(teamId)) return;
        foreach (var t in list)
        {
            t.IsRegistered = await _repo.IsTeamRegisteredAsync(t.Id, teamId);
        }
    }
}
