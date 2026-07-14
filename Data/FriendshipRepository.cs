using Microsoft.EntityFrameworkCore;
using Wallbang.Models;

namespace Wallbang.Data;

public class FriendshipRepository
{
    public async Task<List<User>> GetFriendsAsync(string userId)
    {
        using var db = new WallbangDbContext();
        var asRequester = db.Friendships
            .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.Addressee!);
        var asAddressee = db.Friendships
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.Requester!);

        return await asRequester.Concat(asAddressee).OrderBy(u => u.Nickname).ToListAsync();
    }

    public async Task<List<Friendship>> GetIncomingRequestsAsync(string userId)
    {
        using var db = new WallbangDbContext();
        return await db.Friendships
            .Include(f => f.Requester)
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Friendship>> GetOutgoingRequestsAsync(string userId)
    {
        using var db = new WallbangDbContext();
        return await db.Friendships
            .Include(f => f.Addressee)
            .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Pending)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public enum RelationStatus { None, Friends, OutgoingPending, IncomingPending }

    public async Task<RelationStatus> GetRelationAsync(string viewerId, string otherId)
    {
        if (viewerId == otherId) return RelationStatus.None;
        using var db = new WallbangDbContext();
        var f = await db.Friendships.FirstOrDefaultAsync(x =>
            (x.RequesterId == viewerId && x.AddresseeId == otherId) ||
            (x.RequesterId == otherId && x.AddresseeId == viewerId));
        if (f == null) return RelationStatus.None;
        if (f.Status == FriendshipStatus.Accepted) return RelationStatus.Friends;
        if (f.Status == FriendshipStatus.Pending)
            return f.RequesterId == viewerId ? RelationStatus.OutgoingPending : RelationStatus.IncomingPending;
        return RelationStatus.None;
    }

    public async Task<bool> SendRequestAsync(string requesterId, string addresseeId)
    {
        if (requesterId == addresseeId) return false;
        using var db = new WallbangDbContext();
        var existing = await db.Friendships.FirstOrDefaultAsync(x =>
            (x.RequesterId == requesterId && x.AddresseeId == addresseeId) ||
            (x.RequesterId == addresseeId && x.AddresseeId == requesterId));
        if (existing != null) return false;

        db.Friendships.Add(new Friendship
        {
            Id = $"fr_{Guid.NewGuid():N}",
            RequesterId = requesterId,
            AddresseeId = addresseeId,
            Status = FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AcceptRequestAsync(string friendshipId, string acceptorId)
    {
        using var db = new WallbangDbContext();
        var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == friendshipId);
        if (f == null || f.AddresseeId != acceptorId || f.Status != FriendshipStatus.Pending) return false;
        f.Status = FriendshipStatus.Accepted;
        f.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeclineRequestAsync(string friendshipId, string declinerId)
    {
        using var db = new WallbangDbContext();
        var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == friendshipId);
        if (f == null || f.AddresseeId != declinerId || f.Status != FriendshipStatus.Pending) return false;
        f.Status = FriendshipStatus.Declined;
        f.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveFriendAsync(string userAId, string userBId)
    {
        using var db = new WallbangDbContext();
        var f = await db.Friendships.FirstOrDefaultAsync(x =>
            (x.RequesterId == userAId && x.AddresseeId == userBId) ||
            (x.RequesterId == userBId && x.AddresseeId == userAId));
        if (f == null) return false;
        db.Friendships.Remove(f);
        await db.SaveChangesAsync();
        return true;
    }
}
