namespace Wallbang.Models;

public enum TeamRole
{
    Member = 0,
    ViceCaptain = 1,
    Captain = 2
}

public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Blocked = 3
}

public enum TeamInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Cancelled = 3
}

public enum MatchStatus
{
    Scheduled = 0,
    Live = 1,
    Finished = 2,
    Cancelled = 3
}
