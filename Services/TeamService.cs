using Summit.Data;
using Summit.Models;

namespace Summit.Services;

public class TeamService : Interfaces.ITeamService
{
    private readonly TeamRepository _repo = new();
    private readonly UserRepository _userRepo = new();

    public Task<Team?> GetTeamAsync(string teamId) => _repo.GetByIdAsync(teamId);

    public Task<Team?> GetByTagAsync(string tag) => _repo.GetByTagAsync(tag);

    public Task<List<Team>> GetAllAsync() => _repo.GetAllAsync();

    public Task<Team> CreateTeamAsync(string name, string tag)
    {
        var captainId = App.UserService.CurrentUser?.Id ?? string.Empty;
        return _repo.CreateAsync(name, tag, captainId);
    }

    public async Task<bool> InviteByNicknameAsync(string nickname)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId == null) return false;
        if (me.TeamRole != TeamRole.Captain && me.TeamRole != TeamRole.ViceCaptain) return false;

        var target = await _userRepo.GetByNicknameAsync(nickname);
        if (target == null) return false;

        var inv = await _repo.InviteAsync(me.TeamId, target.Id, me.Id);
        return inv != null;
    }

    public Task<List<TeamInvitation>> GetPendingInvitationsAsync(string userId)
        => _repo.GetInvitationsForUserAsync(userId);

    public async Task<bool> AcceptInvitationAsync(string invitationId)
    {
        var ok = await _repo.AcceptInvitationAsync(invitationId);
        if (ok) await ReloadCurrentUserAsync();
        return ok;
    }

    public Task<bool> DeclineInvitationAsync(string invitationId)
        => _repo.DeclineInvitationAsync(invitationId);

    public async Task<bool> LeaveTeamAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return false;
        var ok = await _repo.LeaveTeamAsync(me.Id);
        if (ok) await ReloadCurrentUserAsync();
        return ok;
    }

    public Task<bool> RemoveMemberAsync(string teamId, string userId) => Task.FromResult(true);

    private async Task ReloadCurrentUserAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return;
        var fresh = await _userRepo.GetByIdAsync(me.Id);
        if (fresh != null) App.UserService.SetCurrentUser(fresh);
    }
}
