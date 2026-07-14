using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Data;

public class BadgeRepository
{
    public async Task<List<Badge>> GetAllAsync()
    {
        using var db = new SummitDbContext();
        return await db.Badges.OrderBy(b => b.Name).ToListAsync();
    }

    public async Task<List<Badge>> GetUnlockedForUserAsync(string userId)
    {
        using var db = new SummitDbContext();
        return await (from ub in db.UserBadges
                      join b in db.Badges on ub.BadgeId equals b.Id
                      where ub.UserId == userId
                      orderby ub.UnlockedAt descending
                      select new Badge
                      {
                          Id = b.Id,
                          Name = b.Name,
                          Description = b.Description,
                          Icon = b.Icon,
                          Rarity = b.Rarity,
                          IsUnlocked = true,
                          UnlockedAt = ub.UnlockedAt
                      }).ToListAsync();
    }

    public async Task<List<Badge>> GetAllWithStateForUserAsync(string userId)
    {
        using var db = new SummitDbContext();
        var unlocked = await db.UserBadges
            .Where(ub => ub.UserId == userId)
            .ToDictionaryAsync(ub => ub.BadgeId, ub => ub.UnlockedAt);

        var all = await db.Badges.OrderBy(b => b.Name).ToListAsync();
        foreach (var badge in all)
        {
            if (unlocked.TryGetValue(badge.Id, out var at))
            {
                badge.IsUnlocked = true;
                badge.UnlockedAt = at;
            }
        }
        return all;
    }
}
