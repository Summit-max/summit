using Microsoft.EntityFrameworkCore;
using System.IO;
using Wallbang.Models;

namespace Wallbang.Data;

public class WallbangDbContext : DbContext
{
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

    public static string DatabasePath { get; } = BuildDbPath();

    private static string BuildDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "Wallbang");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "wallbang.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={DatabasePath}");
    }

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
        tour.Ignore(t => t.MapPool);
        tour.Ignore(t => t.Teams);
        tour.Ignore(t => t.RegisteredTeams);
        tour.Ignore(t => t.MapPoolText);
        tour.Ignore(t => t.TeamsCountText);
        tour.Ignore(t => t.SlotsRemaining);
        tour.Ignore(t => t.SlotsFillPercent);
        tour.Ignore(t => t.StatusLabel);
        tour.Ignore(t => t.CountdownLabel);

        // ───── TOURNAMENT TEAM (join) ─────
        var tt = b.Entity<TournamentTeam>();
        tt.HasKey(x => x.Id);
        tt.HasIndex(x => new { x.TournamentId, x.TeamId }).IsUnique();
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
    }
}
