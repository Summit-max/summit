using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Data;

public class TeamRepository
{
    public async Task<Team?> GetByIdAsync(string teamId)
    {
        using var db = new SummitDbContext();
        return await db.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == teamId);
    }

    public async Task<Team?> GetByTagAsync(string tag)
    {
        using var db = new SummitDbContext();
        return await db.Teams
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Tag.ToLower() == tag.ToLower());
    }

    public async Task<List<Team>> GetAllAsync()
    {
        using var db = new SummitDbContext();
        return await db.Teams
            .Include(t => t.Members)
            .OrderByDescending(t => t.Elo)
            .ToListAsync();
    }

    public async Task<Team> CreateAsync(string name, string tag, string captainId)
    {
        using var db = new SummitDbContext();
        var team = new Team
        {
            Id = $"team_{Guid.NewGuid():N}",
            Name = name,
            Tag = tag,
            CaptainId = captainId,
            CreatedAt = DateTime.UtcNow
        };
        db.Teams.Add(team);

        var captain = await db.Users.FirstOrDefaultAsync(u => u.Id == captainId);
        if (captain != null)
        {
            captain.TeamId = team.Id;
            captain.TeamRole = TeamRole.Captain;
            captain.TeamJoinedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return team;
    }

    public async Task<List<TeamInvitation>> GetInvitationsForUserAsync(string userId)
    {
        using var db = new SummitDbContext();
        return await db.TeamInvitations
            .Include(i => i.Team).ThenInclude(t => t!.Members)
            .Include(i => i.InvitedBy)
            .Where(i => i.InvitedUserId == userId && i.Status == TeamInvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<TeamInvitation?> InviteAsync(string teamId, string invitedUserId, string invitedById)
    {
        using var db = new SummitDbContext();

        var inviter = await db.Users.FirstOrDefaultAsync(u => u.Id == invitedById);
        if (inviter == null) return null;
        if (inviter.TeamId != teamId) return null;
        if (inviter.TeamRole != TeamRole.Captain && inviter.TeamRole != TeamRole.ViceCaptain) return null;

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == invitedUserId);
        if (target == null) return null;
        if (target.TeamId != null) return null;

        var existing = await db.TeamInvitations
            .FirstOrDefaultAsync(i => i.TeamId == teamId
                                   && i.InvitedUserId == invitedUserId
                                   && i.Status == TeamInvitationStatus.Pending);
        if (existing != null) return existing;

        var inv = new TeamInvitation
        {
            Id = $"inv_{Guid.NewGuid():N}",
            TeamId = teamId,
            InvitedUserId = invitedUserId,
            InvitedById = invitedById,
            Status = TeamInvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        db.TeamInvitations.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    public async Task<bool> AcceptInvitationAsync(string invitationId)
    {
        using var db = new SummitDbContext();
        var inv = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == invitationId);
        if (inv == null || inv.Status != TeamInvitationStatus.Pending) return false;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == inv.InvitedUserId);
        if (user == null || user.TeamId != null) return false;

        user.TeamId = inv.TeamId;
        user.TeamRole = TeamRole.Member;
        user.TeamJoinedAt = DateTime.UtcNow;
        inv.Status = TeamInvitationStatus.Accepted;
        inv.RespondedAt = DateTime.UtcNow;

        // cancel other pending invites for this user
        var others = await db.TeamInvitations
            .Where(i => i.InvitedUserId == inv.InvitedUserId
                     && i.Status == TeamInvitationStatus.Pending
                     && i.Id != invitationId)
            .ToListAsync();
        foreach (var o in others)
        {
            o.Status = TeamInvitationStatus.Cancelled;
            o.RespondedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeclineInvitationAsync(string invitationId)
    {
        using var db = new SummitDbContext();
        var inv = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == invitationId);
        if (inv == null || inv.Status != TeamInvitationStatus.Pending) return false;
        inv.Status = TeamInvitationStatus.Declined;
        inv.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> LeaveTeamAsync(string userId)
    {
        using var db = new SummitDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || user.TeamId == null) return false;
        user.TeamId = null;
        user.TeamRole = TeamRole.Member;
        user.TeamJoinedAt = null;
        await db.SaveChangesAsync();
        return true;
    }
}
