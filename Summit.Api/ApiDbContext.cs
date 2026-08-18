using Microsoft.EntityFrameworkCore;
using Summit.Models;

namespace Summit.Api;

public class ApiDbContext : DbContext
{
    public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options) { }

    public DbSet<User>             Users            => Set<User>();
    public DbSet<Team>             Teams            => Set<Team>();
    public DbSet<TeamInvitation>   TeamInvitations  => Set<TeamInvitation>();
    public DbSet<Friendship>       Friendships      => Set<Friendship>();
    public DbSet<Tournament>       Tournaments      => Set<Tournament>();
    public DbSet<TournamentTeam>   TournamentTeams  => Set<TournamentTeam>();
    public DbSet<BracketRound>     BracketRounds    => Set<BracketRound>();
    public DbSet<BracketMatch>     BracketMatches   => Set<BracketMatch>();
    public DbSet<Match>            Matches          => Set<Match>();
    public DbSet<MatchPlayer>      MatchPlayers     => Set<MatchPlayer>();
    public DbSet<Badge>            Badges           => Set<Badge>();
    public DbSet<UserBadge>        UserBadges       => Set<UserBadge>();
    public DbSet<TeamJoinRequest>  TeamJoinRequests => Set<TeamJoinRequest>();
    public DbSet<TournamentLineupPlayer> TournamentLineupPlayers => Set<TournamentLineupPlayer>();
    public DbSet<VetoSession>      VetoSessions     => Set<VetoSession>();
    public DbSet<VetoStep>         VetoSteps        => Set<VetoStep>();
    public DbSet<AuditLog>         AuditLogs        => Set<AuditLog>();
    public DbSet<PoolServer>       PoolServers      => Set<PoolServer>();
    public DbSet<Notification>     Notifications    => Set<Notification>();
    public DbSet<Report>           Reports          => Set<Report>();
    public DbSet<AppConfig>        AppConfigs       => Set<AppConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ───── USER ─────
        var user = b.Entity<User>();
        user.HasKey(u => u.Id);
        user.HasIndex(u => u.SteamId).IsUnique();
        user.HasIndex(u => u.Nickname);
        user.Property(u => u.SteamId).IsRequired();
        user.Property(u => u.Nickname).HasMaxLength(128);
        user.Property(u => u.TeamRole).HasConversion<int>();
        user.Ignore(u => u.IsCaptain);
        user.Ignore(u => u.IsViceCaptain);
        user.Ignore(u => u.CanInvite);
        user.HasOne(u => u.Team)
            .WithMany(t => t.Members)
            .HasForeignKey(u => u.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        // ───── TEAM ─────
        var team = b.Entity<Team>();
        team.HasKey(t => t.Id);
        team.HasIndex(t => t.Tag).IsUnique();
        team.Property(t => t.Name).HasMaxLength(128);
        team.Property(t => t.Tag).HasMaxLength(8);
        team.Ignore(t => t.WinRate);
        team.Ignore(t => t.AverageLevel);
        team.Ignore(t => t.InitialLetter);

        // ───── TEAM INVITATION ─────
        var inv = b.Entity<TeamInvitation>();
        inv.HasKey(i => i.Id);
        inv.HasIndex(i => new { i.TeamId, i.InvitedUserId });
        inv.Property(i => i.Status).HasConversion<int>();
        inv.HasOne(i => i.Team)
            .WithMany(t => t.Invitations)
            .HasForeignKey(i => i.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        inv.HasOne(i => i.InvitedUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedUserId)
            .OnDelete(DeleteBehavior.Cascade);
        inv.HasOne(i => i.InvitedBy)
            .WithMany()
            .HasForeignKey(i => i.InvitedById)
            .OnDelete(DeleteBehavior.NoAction);

        // ───── FRIENDSHIP ─────
        var friend = b.Entity<Friendship>();
        friend.HasKey(f => f.Id);
        friend.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
        friend.Property(f => f.Status).HasConversion<int>();
        friend.HasOne(f => f.Requester)
            .WithMany()
            .HasForeignKey(f => f.RequesterId)
            .OnDelete(DeleteBehavior.Cascade);
        friend.HasOne(f => f.Addressee)
            .WithMany()
            .HasForeignKey(f => f.AddresseeId)
            .OnDelete(DeleteBehavior.NoAction);

        // ───── TOURNAMENT ─────
        var tour = b.Entity<Tournament>();
        tour.HasKey(t => t.Id);
        tour.Property(t => t.Status).HasConversion<int>();
        tour.Property(t => t.Name).HasMaxLength(128);
        tour.Ignore(t => t.IsRegistered);
        tour.Ignore(t => t.MyTeamId);
        tour.Ignore(t => t.HasCheckedIn);
        tour.Ignore(t => t.CanCheckIn);
        tour.Ignore(t => t.IsOrganizer);
        tour.Ignore(t => t.CanEdit);
        tour.Ignore(t => t.MapPool);
        tour.Ignore(t => t.Teams);
        tour.Ignore(t => t.RegisteredTeams);
        tour.Ignore(t => t.MapPoolText);
        tour.Ignore(t => t.TeamsCountText);
        tour.Ignore(t => t.SlotsRemaining);
        tour.Ignore(t => t.SlotsFillPercent);
        tour.Ignore(t => t.StatusLabel);
        tour.Ignore(t => t.CountdownLabel);
        tour.Ignore(t => t.RegistrationClosesAt);
        tour.Ignore(t => t.CheckInOpensAt);
        tour.Ignore(t => t.CheckInClosesAt);
        tour.Ignore(t => t.IsRegistrationOpen);
        tour.Ignore(t => t.IsCheckInOpen);
        tour.Ignore(t => t.RegistrationLabel);
        tour.Property(t => t.FormatType).HasConversion<int>();
        tour.Property(t => t.Series).HasConversion<int>();
        tour.Property(t => t.FinalSeries).HasConversion<int>();

        // ───── TOURNAMENT TEAM (join) ─────
        var tt = b.Entity<TournamentTeam>();
        tt.HasKey(x => x.Id);
        tt.HasIndex(x => new { x.TournamentId, x.TeamId }).IsUnique();
        tt.Property(x => x.CheckIn).HasConversion<int>();
        tt.HasOne(x => x.Tournament)
            .WithMany(t => t.TournamentTeams)
            .HasForeignKey(x => x.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);
        tt.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // ───── BRACKET ROUND ─────
        var round = b.Entity<BracketRound>();
        round.HasKey(r => r.Id);
        round.Property(r => r.Side).HasConversion<int>();
        round.HasOne(r => r.Tournament)
            .WithMany(t => t.Bracket)
            .HasForeignKey(r => r.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // ───── BRACKET MATCH ─────
        var bm = b.Entity<BracketMatch>();
        bm.HasKey(m => m.Id);
        bm.Property(m => m.Status).HasConversion<int>();
        bm.HasOne(m => m.Round)
            .WithMany(r => r.Matches)
            .HasForeignKey(m => m.RoundId)
            .OnDelete(DeleteBehavior.Cascade);
        bm.Ignore(m => m.ScoreAText);
        bm.Ignore(m => m.ScoreBText);
        bm.Ignore(m => m.HasScore);
        bm.Ignore(m => m.AWon);
        bm.Ignore(m => m.BWon);
        bm.Ignore(m => m.IsLive);
        bm.Ignore(m => m.IsFinished);
        bm.Ignore(m => m.TimeLabel);

        // ───── MATCH ─────
        var match = b.Entity<Match>();
        match.HasKey(m => m.Id);
        match.Property(m => m.Status).HasConversion<int>();
        match.Property(m => m.ProvisionState).HasConversion<int>();
        match.Ignore(m => m.Score);
        match.Ignore(m => m.TeamAWon);
        match.Ignore(m => m.TeamBWon);
        match.Ignore(m => m.WinnerTag);

        // ───── MATCH PLAYER ─────
        var mp = b.Entity<MatchPlayer>();
        mp.HasKey(x => x.Id);
        mp.HasIndex(x => new { x.MatchId, x.UserId }).IsUnique();
        mp.HasOne(x => x.Match)
            .WithMany(m => m.Players)
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        mp.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        mp.Ignore(x => x.KD);
        mp.Ignore(x => x.HSPercent);
        mp.Ignore(x => x.KDText);
        mp.Ignore(x => x.HSText);
        mp.Ignore(x => x.ADRText);
        mp.Ignore(x => x.RatingText);
        mp.Ignore(x => x.KDAText);

        // ───── BADGE ─────
        b.Entity<Badge>().HasKey(x => x.Id);
        b.Entity<Badge>().Ignore(x => x.IsUnlocked);
        b.Entity<Badge>().Ignore(x => x.UnlockedAt);

        // ───── USER BADGE (join) ─────
        var ub = b.Entity<UserBadge>();
        ub.HasKey(x => x.Id);
        ub.HasIndex(x => new { x.UserId, x.BadgeId }).IsUnique();
        ub.HasOne(x => x.User)
            .WithMany(u => u.Badges)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        ub.HasOne(x => x.Badge)
            .WithMany()
            .HasForeignKey(x => x.BadgeId)
            .OnDelete(DeleteBehavior.Cascade);

        // ───── TEAM JOIN REQUEST ─────
        var jr = b.Entity<TeamJoinRequest>();
        jr.HasKey(x => x.Id);
        jr.HasIndex(x => new { x.TeamId, x.UserId });
        jr.Property(x => x.Status).HasConversion<int>();
        jr.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
        jr.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ───── TOURNAMENT LINEUP PLAYER ─────
        var lp = b.Entity<TournamentLineupPlayer>();
        lp.HasKey(x => x.Id);
        lp.HasIndex(x => new { x.TournamentTeamId, x.UserId }).IsUnique();
        lp.HasOne(x => x.TournamentTeam)
            .WithMany(t => t.Lineup)
            .HasForeignKey(x => x.TournamentTeamId)
            .OnDelete(DeleteBehavior.Cascade);
        lp.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ───── VETO SESSION / STEPS ─────
        var vs = b.Entity<VetoSession>();
        vs.HasKey(x => x.Id);
        vs.HasIndex(x => x.BracketMatchId).IsUnique();
        vs.Property(x => x.Series).HasConversion<int>();
        vs.Ignore(x => x.MapPool);

        var vstep = b.Entity<VetoStep>();
        vstep.HasKey(x => x.Id);
        vstep.HasIndex(x => new { x.SessionId, x.Order }).IsUnique();
        vstep.Property(x => x.Action).HasConversion<int>();
        vstep.HasOne(x => x.Session)
            .WithMany(s => s.Steps)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ───── POOL SERVER ─────
        var pool = b.Entity<PoolServer>();
        pool.HasKey(x => x.Id);
        pool.Property(x => x.State).HasConversion<int>();

        // ───── AUDIT LOG ─────
        var al = b.Entity<AuditLog>();
        al.HasKey(x => x.Id);
        al.HasIndex(x => x.TeamId);
        al.HasIndex(x => x.TournamentId);
        al.HasIndex(x => x.CreatedAt);

        // ───── NOTIFICATION (fase pós-partida) ─────
        var ntf = b.Entity<Notification>();
        ntf.HasKey(x => x.Id);
        ntf.Property(x => x.Type).HasConversion<int>();
        ntf.HasIndex(x => new { x.UserId, x.IsRead });

        // ───── REPORT (denúncia — fase pós-partida) ─────
        var rpt = b.Entity<Report>();
        rpt.HasKey(x => x.Id);
        rpt.Property(x => x.Status).HasConversion<int>();
        rpt.HasIndex(x => x.Status);

        // ───── APP CONFIG (kill-switch remoto pra builds de teste distribuídas) ─────
        b.Entity<AppConfig>().HasKey(x => x.Id);
    }
}
