using Summit.Models;

namespace Summit.Services.Interfaces;

public interface ITeamService
{
    Task<Team?> GetTeamAsync(string teamId);
    Task<Team> CreateTeamAsync(string name, string tag);
    Task<bool> RemoveMemberAsync(string teamId, string userId);
}
