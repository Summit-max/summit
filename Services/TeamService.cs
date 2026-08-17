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

    public async Task<Team> CreateTeamAsync(string name, string tag)
    {
        var captainId = App.UserService.CurrentUser?.Id ?? string.Empty;
        var team = await _repo.CreateAsync(name, tag, captainId);
        await ReloadCurrentUserAsync();
        return team;
    }

    public async Task<(bool Ok, string? Message)> InviteByNicknameAsync(string nickname)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId == null) return (false, "Você não está em um time.");
        // só o capitão convida (espec-times §3.1/§7) — reflete a mesma regra da API
        if (me.TeamRole != TeamRole.Captain) return (false, "Só o capitão do time pode convidar jogadores.");

        var target = await _userRepo.GetByNicknameAsync(nickname);
        if (target == null) return (false, "Jogador não encontrado.");

        var (ok, _, message) = await _repo.InviteAsync(me.TeamId, target.Id, me.Id);
        return (ok, message);
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

    public async Task<List<TeamJoinRequest>> GetJoinRequestsAsync(string teamId)
    {
        var me = App.UserService.CurrentUser;
        return me == null ? new() : await _repo.GetJoinRequestsAsync(teamId, me.Id);
    }

    public Task<TeamJoinRequest?> RequestToJoinAsync(string teamId, string? message)
    {
        var me = App.UserService.CurrentUser;
        return me == null ? Task.FromResult<TeamJoinRequest?>(null) : _repo.CreateJoinRequestAsync(teamId, me.Id, message);
    }

    public async Task<bool> AcceptJoinRequestAsync(string id)
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return false;
        var ok = await _repo.AcceptJoinRequestAsync(id, me.Id);
        if (ok) await ReloadCurrentUserAsync();
        return ok;
    }

    public Task<bool> DeclineJoinRequestAsync(string id)
    {
        var me = App.UserService.CurrentUser;
        return me == null ? Task.FromResult(false) : _repo.DeclineJoinRequestAsync(id, me.Id);
    }

    public async Task<bool> PromoteAsync(string teamId, string userId)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId == null || !me.IsCaptain) return false;
        return await _repo.PromoteAsync(teamId, userId, me.Id);
    }

    public async Task<bool> DemoteAsync(string teamId, string userId)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId == null || !me.IsCaptain) return false;
        return await _repo.DemoteAsync(teamId, userId, me.Id);
    }

    public async Task<bool> TransferOwnershipAsync(string teamId, string userId)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId == null || !me.IsCaptain) return false;
        var ok = await _repo.TransferOwnershipAsync(teamId, userId, me.Id);
        if (ok) await ReloadCurrentUserAsync();
        return ok;
    }

    public async Task<Team?> UpdateTeamAsync(string teamId, string name, string? description, string? logoUrl, string? country)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId != teamId || !me.IsCaptain) return null;
        return await _repo.UpdateAsync(teamId, name, description, logoUrl, country, me.Id);
    }

    public async Task<(bool Ok, string? Message)> DeleteTeamAsync(string teamId)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId != teamId || !me.IsCaptain) return (false, "Você não é o capitão deste time.");
        var (ok, message) = await _repo.DeleteAsync(teamId, me.Id);
        if (ok) await ReloadCurrentUserAsync();
        return (ok, message);
    }

    public async Task<bool> KickMemberAsync(string teamId, string userId)
    {
        var me = App.UserService.CurrentUser;
        if (me?.TeamId != teamId || !me.IsCaptain) return false;
        return await _repo.KickAsync(teamId, userId, me.Id);
    }

    private async Task ReloadCurrentUserAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return;
        var fresh = await _userRepo.GetByIdAsync(me.Id);
        if (fresh != null) App.UserService.SetCurrentUser(fresh);
    }
}
