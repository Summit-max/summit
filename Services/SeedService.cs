using Microsoft.EntityFrameworkCore;
using Wallbang.Data;
using Wallbang.Models;

namespace Wallbang.Services;

public class SeedService
{
    public async Task EnsureSeededAsync()
    {
        using var db = new WallbangDbContext();

        bool hasUsers = await db.Users.AnyAsync();
        if (hasUsers) return;

        // ───── USERS (16 mocks) ─────
        var now = DateTime.UtcNow;
        var users = new[]
        {
            new User { Id="usr_ghost",  SteamId="76561198000000001", Nickname="xGhostFrag",  AvatarUrl="", Country="🇧🇷", Rank="Global Elite",  Level=120, Elo=3240, WinRate=0.78, KD=1.62, HeadshotPercent=0.58, AvgDamagePerRound=92.4, TotalMatches=412, TotalWins=321, TotalKills=9800, TotalDeaths=6050, TotalAssists=1840, PrimaryRole="Entry", FavoriteMap="Inferno", FavoriteWeapon="AK-47", Bio="Entry fragger desde 2018. Jogo pra abrir o bomb site."},
            new User { Id="usr_s1mple", SteamId="76561198000000002", Nickname="s1mpleKid",   AvatarUrl="", Country="🇺🇦", Rank="Global Elite",  Level=114, Elo=3180, WinRate=0.74, KD=1.55, HeadshotPercent=0.52, AvgDamagePerRound=88.1, TotalMatches=388, TotalWins=287, TotalKills=8920, TotalDeaths=5750, TotalAssists=1610, PrimaryRole="AWPer",  FavoriteMap="Mirage",  FavoriteWeapon="AWP"},
            new User { Id="usr_niko",   SteamId="76561198000000003", Nickname="NikoStyle",   AvatarUrl="", Country="🇧🇦", Rank="Global Elite",  Level=108, Elo=3095, WinRate=0.71, KD=1.48, HeadshotPercent=0.56, AvgDamagePerRound=86.9, TotalMatches=402, TotalWins=285, TotalKills=8600, TotalDeaths=5800, TotalAssists=1520, PrimaryRole="Rifler", FavoriteMap="Dust2",   FavoriteWeapon="M4A4"},
            new User { Id="usr_ropz",   SteamId="76561198000000004", Nickname="ropzFan",     AvatarUrl="", Country="🇪🇪", Rank="Supreme",       Level=102, Elo=2980, WinRate=0.68, KD=1.41, HeadshotPercent=0.54, AvgDamagePerRound=82.6, TotalMatches=356, TotalWins=242, TotalKills=7400, TotalDeaths=5250, TotalAssists=1400, PrimaryRole="Lurker", FavoriteMap="Nuke",    FavoriteWeapon="M4A1-S"},
            new User { Id="usr_blast",  SteamId="76561198000000005", Nickname="blastPRO",    AvatarUrl="", Country="🇩🇰", Rank="Supreme",       Level=96,  Elo=2910, WinRate=0.67, KD=1.38, HeadshotPercent=0.49, AvgDamagePerRound=80.1, TotalMatches=344, TotalWins=230, TotalKills=7200, TotalDeaths=5220, TotalAssists=1450, PrimaryRole="IGL",    FavoriteMap="Ancient", FavoriteWeapon="AK-47"},
            new User { Id="usr_zywoo",  SteamId="76561198000000006", Nickname="zywoo_br",    AvatarUrl="", Country="🇫🇷", Rank="Supreme",       Level=90,  Elo=2855, WinRate=0.66, KD=1.36, HeadshotPercent=0.48, AvgDamagePerRound=81.2, TotalMatches=378, TotalWins=249, TotalKills=7550, TotalDeaths=5550, TotalAssists=1380, PrimaryRole="AWPer",  FavoriteMap="Mirage",  FavoriteWeapon="AWP"},
            new User { Id="usr_shox",   SteamId="76561198000000007", Nickname="shoxie_jr",   AvatarUrl="", Country="🇫🇷", Rank="Supreme",       Level=84,  Elo=2790, WinRate=0.65, KD=1.32, HeadshotPercent=0.55, AvgDamagePerRound=78.9, TotalMatches=301, TotalWins=196, TotalKills=5900, TotalDeaths=4470, TotalAssists=1100, PrimaryRole="Rifler", FavoriteMap="Inferno", FavoriteWeapon="AK-47"},
            new User { Id="usr_device", SteamId="76561198000000008", Nickname="device_x",    AvatarUrl="", Country="🇩🇰", Rank="Supreme",       Level=78,  Elo=2740, WinRate=0.64, KD=1.30, HeadshotPercent=0.46, AvgDamagePerRound=77.1, TotalMatches=325, TotalWins=208, TotalKills=6200, TotalDeaths=4770, TotalAssists=1150, PrimaryRole="AWPer",  FavoriteMap="Anubis",  FavoriteWeapon="AWP"},
            new User { Id="usr_fallen", SteamId="76561198000000009", Nickname="FalleN__",    AvatarUrl="", Country="🇧🇷", Rank="Legendary",     Level=72,  Elo=2690, WinRate=0.63, KD=1.28, HeadshotPercent=0.50, AvgDamagePerRound=75.9, TotalMatches=290, TotalWins=183, TotalKills=5600, TotalDeaths=4370, TotalAssists=1050, PrimaryRole="AWPer",  FavoriteMap="Nuke",    FavoriteWeapon="AWP"},
            new User { Id="usr_cold",   SteamId="76561198000000010", Nickname="coldzera",    AvatarUrl="", Country="🇧🇷", Rank="Legendary",     Level=66,  Elo=2640, WinRate=0.62, KD=1.26, HeadshotPercent=0.53, AvgDamagePerRound=74.7, TotalMatches=302, TotalWins=187, TotalKills=5800, TotalDeaths=4600, TotalAssists=980, PrimaryRole="Rifler", FavoriteMap="Dust2",   FavoriteWeapon="AK-47"},
            new User { Id="usr_fer",    SteamId="76561198000000011", Nickname="fer_zera",    AvatarUrl="", Country="🇧🇷", Rank="Legendary",     Level=60,  Elo=2590, WinRate=0.61, KD=1.24, HeadshotPercent=0.57, AvgDamagePerRound=73.9, TotalMatches=278, TotalWins=170, TotalKills=5300, TotalDeaths=4270, TotalAssists=890, PrimaryRole="Entry",  FavoriteMap="Mirage",  FavoriteWeapon="AK-47"},
            new User { Id="usr_taco",   SteamId="76561198000000012", Nickname="taco_br",     AvatarUrl="", Country="🇧🇷", Rank="Legendary",     Level=54,  Elo=2540, WinRate=0.60, KD=1.22, HeadshotPercent=0.44, AvgDamagePerRound=72.5, TotalMatches=266, TotalWins=160, TotalKills=5000, TotalDeaths=4100, TotalAssists=920, PrimaryRole="Support",FavoriteMap="Ancient", FavoriteWeapon="AK-47"},
            new User { Id="usr_yuurih", SteamId="76561198000000013", Nickname="yuurih",      AvatarUrl="", Country="🇧🇷", Rank="Legendary",     Level=48,  Elo=2490, WinRate=0.59, KD=1.20, HeadshotPercent=0.49, AvgDamagePerRound=71.2, TotalMatches=254, TotalWins=150, TotalKills=4800, TotalDeaths=4000, TotalAssists=860, PrimaryRole="Rifler", FavoriteMap="Vertigo", FavoriteWeapon="M4A4"},
            new User { Id="usr_kscer",  SteamId="76561198000000014", Nickname="KSCERATO",    AvatarUrl="", Country="🇧🇷", Rank="Legendary",     Level=42,  Elo=2440, WinRate=0.58, KD=1.18, HeadshotPercent=0.61, AvgDamagePerRound=69.8, TotalMatches=248, TotalWins=144, TotalKills=4700, TotalDeaths=3980, TotalAssists=810, PrimaryRole="Rifler", FavoriteMap="Mirage",  FavoriteWeapon="AK-47"},
            new User { Id="usr_art",    SteamId="76561198000000015", Nickname="arT",         AvatarUrl="", Country="🇧🇷", Rank="Distinguished", Level=36,  Elo=2390, WinRate=0.57, KD=1.16, HeadshotPercent=0.47, AvgDamagePerRound=68.1, TotalMatches=232, TotalWins=132, TotalKills=4400, TotalDeaths=3800, TotalAssists=790, PrimaryRole="IGL",    FavoriteMap="Overpass",FavoriteWeapon="M4A1-S"},
            new User { Id="usr_rookie", SteamId="76561198000000016", Nickname="newcomer42",  AvatarUrl="", Country="🇧🇷", Rank="Silver",        Level=8,   Elo=820,  WinRate=0.48, KD=0.92, HeadshotPercent=0.35, AvgDamagePerRound=58.2, TotalMatches=42,  TotalWins=20,  TotalKills=620,  TotalDeaths=670,  TotalAssists=180, PrimaryRole="Rifler", FavoriteMap="Dust2",   FavoriteWeapon="AK-47"},
        };
        foreach (var u in users) { u.CreatedAt = now.AddDays(-180); u.LastLoginAt = now.AddHours(-2); }
        await db.Users.AddRangeAsync(users);
        await db.SaveChangesAsync();

        // ───── TEAMS ─────
        var navi = new Team { Id="team_navi", Name="NAVI Academy", Tag="NAVI", Country="🇺🇦", CaptainId="usr_ghost",  Elo=3200, CreatedAt=now.AddDays(-300), TournamentsPlayed=12, TournamentsWon=4, MatchesPlayed=185, MatchesWon=128, Description="Top BR/EU. 5 em ponto, jogamos juntos há 2 anos." };
        var faze = new Team { Id="team_faze", Name="FaZe Clan",    Tag="FAZE", Country="🇺🇸", CaptainId="usr_niko",   Elo=3150, CreatedAt=now.AddDays(-280), TournamentsPlayed=10, TournamentsWon=3, MatchesPlayed=172, MatchesWon=113, Description="Mixed roster NA+EU. Foco em mapas de ritmo alto." };
        var vit  = new Team { Id="team_vit",  Name="Vitality",     Tag="VIT",  Country="🇫🇷", CaptainId="usr_zywoo",  Elo=3080, CreatedAt=now.AddDays(-260), TournamentsPlayed=9,  TournamentsWon=2, MatchesPlayed=160, MatchesWon=102, Description="Disciplina francesa. Zywoo carrega." };
        var fur  = new Team { Id="team_fur",  Name="Furia BR",     Tag="FURIA",Country="🇧🇷", CaptainId="usr_art",    Elo=2950, CreatedAt=now.AddDays(-240), TournamentsPlayed=8,  TournamentsWon=2, MatchesPlayed=148, MatchesWon=88,  Description="Ritmo brasileiro. Aggressive plays." };
        var ast  = new Team { Id="team_ast",  Name="Astralis",     Tag="AST",  Country="🇩🇰", CaptainId="usr_device", Elo=2880, CreatedAt=now.AddDays(-220), TournamentsPlayed=7,  TournamentsWon=1, MatchesPlayed=132, MatchesWon=78,  Description="Defesa sólida, taticismo dinamarquês." };
        var imp  = new Team { Id="team_imp",  Name="Imperial",     Tag="IMP",  Country="🇧🇷", CaptainId="usr_fallen", Elo=2700, CreatedAt=now.AddDays(-200), TournamentsPlayed=6,  TournamentsWon=1, MatchesPlayed=118, MatchesWon=65,  Description="Lenda BR. FalleN comanda." };
        var teams = new[] { navi, faze, vit, fur, ast, imp };
        await db.Teams.AddRangeAsync(teams);
        await db.SaveChangesAsync();

        // ───── ASSIGN USERS TO TEAMS ─────
        void Join(string userId, string teamId, TeamRole role)
        {
            var u = users.First(x => x.Id == userId);
            u.TeamId = teamId;
            u.TeamRole = role;
            u.TeamJoinedAt = now.AddDays(-120);
        }
        Join("usr_ghost",  "team_navi", TeamRole.Captain);
        Join("usr_s1mple", "team_navi", TeamRole.ViceCaptain);
        Join("usr_ropz",   "team_navi", TeamRole.Member);
        Join("usr_blast",  "team_navi", TeamRole.Member);
        Join("usr_kscer",  "team_navi", TeamRole.Member);

        Join("usr_niko",   "team_faze", TeamRole.Captain);
        Join("usr_shox",   "team_faze", TeamRole.ViceCaptain);
        Join("usr_cold",   "team_faze", TeamRole.Member);

        Join("usr_zywoo",  "team_vit",  TeamRole.Captain);
        Join("usr_fer",    "team_vit",  TeamRole.Member);

        Join("usr_art",    "team_fur",  TeamRole.Captain);
        Join("usr_yuurih", "team_fur",  TeamRole.ViceCaptain);
        Join("usr_taco",   "team_fur",  TeamRole.Member);

        Join("usr_device", "team_ast",  TeamRole.Captain);

        Join("usr_fallen", "team_imp",  TeamRole.Captain);
        await db.SaveChangesAsync();

        // ───── TOURNAMENTS ─────
        var t1 = new Tournament { Id="trn_cup1",   Name="Wallbang Cup #1",           Format="5v5 • Single Elimination • BO3",     Status=TournamentStatus.Open,       Prize="R$ 5.000",  MaxTeams=8,  StartDate=now.AddDays(7),   Description="Primeiro torneio oficial. Servers on-demand AWS.", Rules="BO3. Veto alternado. Regras HLTV.",      Organizer="Wallbang Staff", MapPoolCsv="Mirage, Inferno, Nuke, Ancient, Anubis, Dust2, Vertigo" };
        var t2 = new Tournament { Id="trn_ranked", Name="Ranked Series — Season 1",  Format="5v5 • Round Robin + Playoffs",       Status=TournamentStatus.InProgress, Prize="R$ 2.000",  MaxTeams=4,  StartDate=now.AddDays(-5), EndDate=now.AddDays(10), Description="Liga semanal com pontuação acumulada.", Rules="BO1 grupos, BO3 playoffs.",    Organizer="Wallbang Staff", MapPoolCsv="Mirage, Inferno, Nuke, Ancient, Anubis" };
        var t3 = new Tournament { Id="trn_newbie", Name="Newcomer Bowl",             Format="5v5 • Single Elimination • BO1",     Status=TournamentStatus.Open,       Prize="R$ 500",    MaxTeams=16, StartDate=now.AddDays(14),  Description="Torneio pra times novos. Nivel médio <= 25.", Rules="BO1. Nivel medio do time <=25.", Organizer="Wallbang Staff", MapPoolCsv="Mirage, Dust2, Inferno" };
        var t4 = new Tournament { Id="trn_pro",    Name="Pro Invitational",          Format="5v5 • GSL Groups + Playoffs",        Status=TournamentStatus.Upcoming,   Prize="R$ 20.000", MaxTeams=8,  StartDate=now.AddDays(30),  Description="Por convite para os 8 melhores do ranking.", Rules="GSL duplo, playoffs BO5.",      Organizer="Wallbang Staff", MapPoolCsv="Mirage, Inferno, Nuke, Ancient, Anubis, Dust2, Vertigo" };
        await db.Tournaments.AddRangeAsync(t1, t2, t3, t4);
        await db.SaveChangesAsync();

        // ───── TOURNAMENT TEAMS (registrations) ─────
        string[] t1TeamIds = { "team_navi", "team_faze", "team_vit", "team_fur", "team_ast", "team_imp" };
        for (int i = 0; i < t1TeamIds.Length; i++)
            db.TournamentTeams.Add(new TournamentTeam
            {
                Id = $"tt_t1_{i}", TournamentId = t1.Id, TeamId = t1TeamIds[i], Seed = i + 1, RegisteredAt = now.AddDays(-3)
            });

        string[] t2TeamIds = { "team_navi", "team_faze", "team_vit", "team_fur" };
        for (int i = 0; i < t2TeamIds.Length; i++)
            db.TournamentTeams.Add(new TournamentTeam
            {
                Id = $"tt_t2_{i}", TournamentId = t2.Id, TeamId = t2TeamIds[i], Seed = i + 1, RegisteredAt = now.AddDays(-20)
            });
        await db.SaveChangesAsync();

        // ───── BRACKET (Cup #1) ─────
        var r1 = new BracketRound { Id="rnd_t1_1", TournamentId=t1.Id, RoundNumber=1, Name="QUARTAS" };
        var r2 = new BracketRound { Id="rnd_t1_2", TournamentId=t1.Id, RoundNumber=2, Name="SEMIS" };
        var r3 = new BracketRound { Id="rnd_t1_3", TournamentId=t1.Id, RoundNumber=3, Name="FINAL" };
        await db.BracketRounds.AddRangeAsync(r1, r2, r3);

        await db.BracketMatches.AddRangeAsync(
            new BracketMatch { Id="bm_t1_q1", RoundId=r1.Id, Position=1, TeamATag="NAVI",  TeamBTag="IMP",   Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate },
            new BracketMatch { Id="bm_t1_q2", RoundId=r1.Id, Position=2, TeamATag="AST",   TeamBTag="FURIA", Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate.AddHours(1) },
            new BracketMatch { Id="bm_t1_q3", RoundId=r1.Id, Position=3, TeamATag="VIT",   TeamBTag="FAZE",  Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate.AddHours(2) },
            new BracketMatch { Id="bm_t1_q4", RoundId=r1.Id, Position=4, TeamATag="TBD",   TeamBTag="TBD",   Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate.AddHours(3) },
            new BracketMatch { Id="bm_t1_s1", RoundId=r2.Id, Position=1, TeamATag="TBD",   TeamBTag="TBD",   Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate.AddDays(1) },
            new BracketMatch { Id="bm_t1_s2", RoundId=r2.Id, Position=2, TeamATag="TBD",   TeamBTag="TBD",   Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate.AddDays(1).AddHours(1) },
            new BracketMatch { Id="bm_t1_f",  RoundId=r3.Id, Position=1, TeamATag="TBD",   TeamBTag="TBD",   Status=BracketMatchStatus.Pending, ScheduledAt=t1.StartDate.AddDays(2) }
        );

        // ───── BRACKET (Ranked S1 — already started) ─────
        var r2s1 = new BracketRound { Id="rnd_t2_1", TournamentId=t2.Id, RoundNumber=1, Name="SEMIS" };
        var r2s2 = new BracketRound { Id="rnd_t2_2", TournamentId=t2.Id, RoundNumber=2, Name="FINAL" };
        await db.BracketRounds.AddRangeAsync(r2s1, r2s2);
        await db.BracketMatches.AddRangeAsync(
            new BracketMatch { Id="bm_t2_s1", RoundId=r2s1.Id, Position=1, TeamATag="NAVI", TeamBTag="FURIA", ScoreA=2, ScoreB=1, Status=BracketMatchStatus.Finished, ScheduledAt=now.AddDays(-2) },
            new BracketMatch { Id="bm_t2_s2", RoundId=r2s1.Id, Position=2, TeamATag="FAZE", TeamBTag="VIT",   ScoreA=1, ScoreB=2, Status=BracketMatchStatus.Live,     ScheduledAt=now },
            new BracketMatch { Id="bm_t2_f",  RoundId=r2s2.Id, Position=1, TeamATag="NAVI", TeamBTag="TBD",                      Status=BracketMatchStatus.Pending,  ScheduledAt=now.AddDays(2) }
        );

        await db.SaveChangesAsync();

        // ───── MATCHES (with full stats) ─────
        await SeedMatchesAsync(db, now, t2);

        // ───── BADGES ─────
        var badges = new[]
        {
            new Badge { Id="bd_firstwin",  Name="First Blood",       Description="Sua primeira vitória.",                 Icon="", Rarity="Common" },
            new Badge { Id="bd_clutch",    Name="Clutch King",       Description="Clutch 1v4 ou 1v5.",                     Icon="", Rarity="Rare" },
            new Badge { Id="bd_ace",       Name="ACE!",              Description="5 kills num round.",                     Icon="", Rarity="Rare" },
            new Badge { Id="bd_mvp",       Name="MVP",               Description="MVP em 10 partidas.",                    Icon="", Rarity="Epic" },
            new Badge { Id="bd_champion",  Name="Campeão",           Description="Venceu um campeonato oficial.",          Icon="", Rarity="Legendary" },
            new Badge { Id="bd_hunter",    Name="Headhunter",        Description="HS% acima de 50% em 30 partidas.",       Icon="", Rarity="Epic" },
            new Badge { Id="bd_loyal",     Name="Leal",              Description="1 ano no mesmo time.",                   Icon="", Rarity="Rare" },
            new Badge { Id="bd_founder",   Name="Fundador",          Description="Conta criada na beta da Wallbang.",      Icon="", Rarity="Legendary" },
        };
        await db.Badges.AddRangeAsync(badges);
        await db.SaveChangesAsync();

        // Unlock some for top players
        var unlocks = new[]
        {
            ("usr_ghost",  "bd_firstwin"), ("usr_ghost","bd_ace"), ("usr_ghost","bd_mvp"), ("usr_ghost","bd_champion"), ("usr_ghost","bd_hunter"), ("usr_ghost","bd_founder"),
            ("usr_s1mple", "bd_firstwin"), ("usr_s1mple","bd_ace"), ("usr_s1mple","bd_mvp"), ("usr_s1mple","bd_clutch"),
            ("usr_niko",   "bd_firstwin"), ("usr_niko","bd_mvp"),
            ("usr_zywoo",  "bd_firstwin"), ("usr_zywoo","bd_clutch"), ("usr_zywoo","bd_hunter"),
            ("usr_fallen", "bd_firstwin"), ("usr_fallen","bd_champion"), ("usr_fallen","bd_loyal"),
        };
        foreach (var (uid, bid) in unlocks)
        {
            db.UserBadges.Add(new UserBadge
            {
                Id = $"ub_{uid}_{bid}", UserId = uid, BadgeId = bid, UnlockedAt = now.AddDays(-30)
            });
        }

        // ───── FRIENDSHIPS ─────
        var friendships = new[]
        {
            ("usr_ghost",  "usr_s1mple", FriendshipStatus.Accepted),
            ("usr_ghost",  "usr_ropz",   FriendshipStatus.Accepted),
            ("usr_ghost",  "usr_niko",   FriendshipStatus.Accepted),
            ("usr_s1mple", "usr_ropz",   FriendshipStatus.Accepted),
            ("usr_niko",   "usr_shox",   FriendshipStatus.Accepted),
            ("usr_fallen", "usr_cold",   FriendshipStatus.Accepted),
            ("usr_fallen", "usr_fer",    FriendshipStatus.Accepted),
            ("usr_art",    "usr_yuurih", FriendshipStatus.Accepted),
            ("usr_rookie", "usr_ghost",  FriendshipStatus.Pending),
            ("usr_kscer",  "usr_ghost",  FriendshipStatus.Pending),
        };
        foreach (var (a, c, s) in friendships)
        {
            db.Friendships.Add(new Friendship
            {
                Id = $"fr_{a}_{c}",
                RequesterId = a,
                AddresseeId = c,
                Status = s,
                CreatedAt = now.AddDays(-10),
                RespondedAt = s == FriendshipStatus.Accepted ? now.AddDays(-9) : null
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedMatchesAsync(WallbangDbContext db, DateTime now, Tournament liveTournament)
    {
        // Matches with scoreboards. Each match has 10 players (5 per side).
        var matches = new List<(string id, string map, DateTime when, string taTag, string tbTag, string taId, string tbId, int sa, int sb, int duration, string[] sideA, string[] sideB, string? bracketMatchId, string? tournamentId, string? tournamentName)>
        {
            ("m_001","Inferno",now.AddDays(-2),  "NAVI","FURIA","team_navi","team_fur",16,13,43,
                new[]{"usr_ghost","usr_s1mple","usr_ropz","usr_blast","usr_kscer"},
                new[]{"usr_art","usr_yuurih","usr_taco","usr_fallen","usr_fer"},
                "bm_t2_s1", liveTournament.Id, liveTournament.Name),

            ("m_002","Mirage",now.AddDays(-3),   "FAZE","VIT", "team_faze","team_vit",14,16,42,
                new[]{"usr_niko","usr_shox","usr_cold","usr_rookie","usr_taco"},
                new[]{"usr_zywoo","usr_fer","usr_yuurih","usr_kscer","usr_ropz"},
                null, null, null),

            ("m_003","Nuke",   now.AddDays(-5),  "NAVI","AST", "team_navi","team_ast",16,8,38,
                new[]{"usr_ghost","usr_s1mple","usr_ropz","usr_blast","usr_kscer"},
                new[]{"usr_device","usr_cold","usr_fer","usr_taco","usr_rookie"},
                null, null, null),

            ("m_004","Ancient",now.AddDays(-7),  "IMP", "NAVI","team_imp","team_navi",11,16,41,
                new[]{"usr_fallen","usr_cold","usr_fer","usr_art","usr_taco"},
                new[]{"usr_ghost","usr_s1mple","usr_ropz","usr_blast","usr_kscer"},
                null, null, null),

            ("m_005","Dust2",  now.AddDays(-9),  "FURIA","FAZE","team_fur","team_faze",16,10,40,
                new[]{"usr_art","usr_yuurih","usr_taco","usr_fallen","usr_fer"},
                new[]{"usr_niko","usr_shox","usr_cold","usr_rookie","usr_ghost"},
                null, null, null),

            ("m_006","Anubis", now.AddDays(-11), "VIT","NAVI","team_vit","team_navi",13,16,44,
                new[]{"usr_zywoo","usr_fer","usr_yuurih","usr_shox","usr_cold"},
                new[]{"usr_ghost","usr_s1mple","usr_ropz","usr_blast","usr_kscer"},
                null, null, null),

            ("m_007","Vertigo",now.AddDays(-13), "AST","IMP","team_ast","team_imp",16,14,46,
                new[]{"usr_device","usr_cold","usr_fer","usr_taco","usr_rookie"},
                new[]{"usr_fallen","usr_art","usr_yuurih","usr_taco","usr_kscer"},
                null, null, null),

            ("m_008","Inferno",now.AddDays(-16), "NAVI","VIT","team_navi","team_vit",7,16,45,
                new[]{"usr_ghost","usr_s1mple","usr_ropz","usr_blast","usr_kscer"},
                new[]{"usr_zywoo","usr_fer","usr_yuurih","usr_shox","usr_cold"},
                null, null, null),
        };

        foreach (var m in matches)
        {
            // avoid duplicate userId in a side (some seed entries reuse names accidentally)
            var sideA = m.sideA.Distinct().Take(5).ToArray();
            var sideB = m.sideB.Distinct().Where(u => !sideA.Contains(u)).Take(5).ToArray();

            var match = new Match
            {
                Id = m.id, Map = m.map, PlayedAt = m.when, Status = MatchStatus.Finished,
                DurationMinutes = m.duration,
                TeamAId = m.taId, TeamBId = m.tbId,
                TeamATag = m.taTag, TeamBTag = m.tbTag,
                TeamAName = m.taTag, TeamBName = m.tbTag,
                ScoreA = m.sa, ScoreB = m.sb,
                BracketMatchId = m.bracketMatchId,
                TournamentId = m.tournamentId,
                TournamentName = m.tournamentName
            };
            db.Matches.Add(match);

            var rng = new Random(m.id.GetHashCode());
            int totalRounds = m.sa + m.sb;

            foreach (var uid in sideA)
            {
                var mvp = (uid == sideA[0]) && m.sa > m.sb;
                db.MatchPlayers.Add(BuildPlayerStat(m.id, uid, "A", totalRounds, rng, mvp));
            }
            foreach (var uid in sideB)
            {
                var mvp = (uid == sideB[0]) && m.sb > m.sa;
                db.MatchPlayers.Add(BuildPlayerStat(m.id, uid, "B", totalRounds, rng, mvp));
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchPlayer BuildPlayerStat(string matchId, string userId, string side, int rounds, Random rng, bool mvp)
    {
        int kills    = rng.Next(8, 28);
        int deaths   = rng.Next(10, 22);
        int assists  = rng.Next(2, 10);
        int hsKills  = (int)(kills * (0.35 + rng.NextDouble() * 0.35));
        double adr   = 55 + rng.NextDouble() * 50;
        double rating = 0.6 + rng.NextDouble() * 1.0;
        return new MatchPlayer
        {
            Id = $"mp_{matchId}_{userId}",
            MatchId = matchId, UserId = userId, TeamSide = side,
            Kills = kills, Deaths = deaths, Assists = assists,
            HeadshotKills = hsKills,
            AvgDamagePerRound = Math.Round(adr, 1),
            Rating = Math.Round(rating, 2),
            IsMvp = mvp
        };
    }
}
