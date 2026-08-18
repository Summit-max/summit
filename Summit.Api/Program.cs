using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Summit.Api;
using Summit.Models;

var builder = WebApplication.CreateBuilder(args);

// ───── Banco: MySQL (env SUMMIT_DB ou appsettings), senão SQLite local (dev) ─────
var mysql = Environment.GetEnvironmentVariable("SUMMIT_DB")
         ?? builder.Configuration.GetConnectionString("MySql");

builder.Services.AddDbContext<ApiDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(mysql))
        o.UseMySql(mysql, ServerVersion.AutoDetect(mysql));
    else
        o.UseSqlite($"Data Source={Path.Combine(builder.Environment.ContentRootPath, "summit-api.db")}");
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// motor do ciclo de vida dos campeonatos (check-in, chave, início, vetos)
builder.Services.AddHostedService<LifecycleWorker>();

// provisionamento de servidores CS2 na AWS (docs/plano-aws.md)
builder.Services.AddSingleton<MatchServerService>();
builder.Services.AddHostedService<ServerProvisionPoller>();
builder.Services.AddHostedService<PoolManagerService>();

// provider de servidor de partida — plugável (docs/spec/summit-fase-final/plan.md RF-00).
// "local" (padrão, sem AWS) ou "aws" (real) via SUMMIT_MATCH_PROVIDER.
var matchProvider = Environment.GetEnvironmentVariable("SUMMIT_MATCH_PROVIDER") ?? "local";
if (matchProvider == "aws")
    builder.Services.AddSingleton<IMatchServerProvider, AwsMatchServerProvider>();
else
    builder.Services.AddSingleton<IMatchServerProvider, LocalSimulatedMatchServerProvider>();

// autenticação real (Fase A do plano de produção) — token emitido em POST /api/users/steam-login
// depois do login Steam de verdade (SteamAuthService.cs já verifica contra a Steam).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = SummitAuth.Issuer,
            ValidateAudience = true,
            ValidAudience = SummitAuth.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SummitAuth.SigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();

// endpoints de debug (/api/debug/*) só existem com essa env var — sem ela, 404 (não só
// bloqueado). Precisa setar junto com SUMMIT_MATCH_PROVIDER etc. ao rodar localmente.
var enableDebugEndpoints = Environment.GetEnvironmentVariable("SUMMIT_ENABLE_DEBUG") == "true";

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// cria schema + seed de demonstração
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();
    await SeedData.EnsureSeededAsync(db);
}

app.MapGet("/", () => Results.Ok(new
{
    name = "Summit API",
    status = "ok",
    database = string.IsNullOrWhiteSpace(mysql) ? "sqlite (dev)" : "mysql",
}));

// kill-switch remoto pra builds distribuídas (ex: teste com times externos) — o client confere
// isso na tela de abertura antes de logar; ativado por padrão. Pra desligar depois de um teste,
// atualiza a linha 'singleton' de AppConfigs direto no banco (não precisa de redeploy).
app.MapGet("/api/app/status", async (ApiDbContext db) =>
{
    var cfg = await db.AppConfigs.FindAsync("singleton");
    return Results.Ok(new AppStatus
    {
        Active = cfg?.TestActive ?? true,
        Message = cfg?.Message ?? ""
    });
});

// Endpoints de debug (Fase A do plano de produção) — só existem com SUMMIT_ENABLE_DEBUG=true,
// senão nem são mapeados (404, não só bloqueados). Nunca vão pra um deploy real com essa env var.
if (enableDebugEndpoints)
{

// diagnostico: lista as AMIs próprias da conta — usado quando SUMMIT_AMI_ID configurada
// não existe mais (foi trocada/deregistrada) e precisa achar a atual.
app.MapGet("/api/debug/my-amis", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeImagesAsync(new Amazon.EC2.Model.DescribeImagesRequest
    {
        Owners = new List<string> { "self" }
    });
    return Results.Ok(resp.Images.OrderByDescending(i => i.CreationDate).Select(i => new
    {
        i.ImageId,
        i.Name,
        i.CreationDate,
        State = i.State.Value
    }));
});

// diagnostico: custo real do RDS/Aurora dia a dia, quebrado por tipo de uso (storage,
// ACU-hora, I/O, etc.) — pra responder "por que gastou X" com dado de verdade, não chute.
app.MapGet("/api/debug/rds-cost", async (int? days) =>
{
    using var ce = new Amazon.CostExplorer.AmazonCostExplorerClient(Amazon.RegionEndpoint.USEast1);
    var n = days ?? 3;
    var end = DateTime.UtcNow.Date.AddDays(1); // CE end é exclusivo
    var start = end.AddDays(-n - 1);
    var resp = await ce.GetCostAndUsageAsync(new Amazon.CostExplorer.Model.GetCostAndUsageRequest
    {
        TimePeriod = new Amazon.CostExplorer.Model.DateInterval
        { Start = start.ToString("yyyy-MM-dd"), End = end.ToString("yyyy-MM-dd") },
        Granularity = Amazon.CostExplorer.Granularity.DAILY,
        Metrics = new List<string> { "UnblendedCost" },
        Filter = new Amazon.CostExplorer.Model.Expression
        {
            Dimensions = new Amazon.CostExplorer.Model.DimensionValues
            {
                Key = Amazon.CostExplorer.Dimension.SERVICE,
                Values = new List<string> { "Amazon Relational Database Service" }
            }
        },
        GroupBy = new List<Amazon.CostExplorer.Model.GroupDefinition>
        {
            new() { Type = Amazon.CostExplorer.GroupDefinitionType.DIMENSION, Key = "USAGE_TYPE" }
        }
    });
    return Results.Ok(resp.ResultsByTime.Select(r => new
    {
        r.TimePeriod.Start,
        r.TimePeriod.End,
        Total = r.Total.TryGetValue("UnblendedCost", out var t) ? t.Amount : null,
        Groups = r.Groups
            .Select(g => new { Key = string.Join(",", g.Keys), Amount = g.Metrics["UnblendedCost"].Amount })
            .Where(g => double.TryParse(g.Amount, out var a) && a > 0)
            .OrderByDescending(g => double.Parse(g.Amount))
    }));
});

// diagnostico: confere se a AMI configurada ja esta pronta pra uso (evita ficar
// checando manualmente no console da AWS enquanto ela empacota o disco)
app.MapGet("/api/debug/ami-status", async () =>
{
    var amiId = Environment.GetEnvironmentVariable("SUMMIT_AMI_ID");
    if (string.IsNullOrWhiteSpace(amiId)) return Results.Ok(new { configured = false });

    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    try
    {
        var resp = await ec2.DescribeImagesAsync(new Amazon.EC2.Model.DescribeImagesRequest
        {
            ImageIds = new List<string> { amiId }
        });
        var img = resp.Images.FirstOrDefault();
        return Results.Ok(new
        {
            configured = true,
            amiId,
            state = img?.State?.Value ?? "not-found",
            progress = img?.BlockDeviceMappings?.Count > 0 ? "ok" : null
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { configured = true, amiId, error = ex.Message });
    }
});

// diagnostico: lista toda instância EC2 com a tag summit:matchId (inclusive
// órfãs sem linha em `matches`, ex. quando o teste é resetado no meio do provisionamento)
app.MapPost("/api/debug/instances/{instanceId}/terminate", async (string instanceId, MatchServerService server) =>
{
    await server.TerminateAsync(instanceId);
    return Results.Ok(new { terminated = instanceId });
});

app.MapPost("/api/debug/instances/{instanceId}/stop", async (string instanceId, MatchServerService server) =>
{
    await server.StopAsync(instanceId);
    return Results.Ok(new { stopped = instanceId });
});

app.MapGet("/api/debug/instances", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeInstancesAsync(new Amazon.EC2.Model.DescribeInstancesRequest
    {
        Filters = new List<Amazon.EC2.Model.Filter>
        {
            new() { Name = "tag-key", Values = new List<string> { "summit:matchId", "summit:pool", "summit:manual", "summit:apihost" } }
        }
    });
    var list = resp.Reservations.SelectMany(r => r.Instances)
        .Where(i => i.State.Name != Amazon.EC2.InstanceStateName.Terminated)
        .Select(i => new
        {
            instanceId = i.InstanceId,
            state = i.State.Name.Value,
            publicIp = i.PublicIpAddress,
            launchTime = i.LaunchTime,
            matchId = i.Tags.FirstOrDefault(t => t.Key == "summit:matchId")?.Value
        });
    return Results.Ok(list);
});

// diagnostico: manda um comando RCON ad-hoc pra um servidor do pool (teste manual, ex. "meta list")
app.MapPost("/api/debug/rcon", async (ApiDbContext db, RconDebugRequest body) =>
{
    var poolServer = await db.PoolServers.FirstOrDefaultAsync(p => p.PublicIp == body.Ip);
    var password = poolServer?.RconPassword ?? body.Password;
    if (string.IsNullOrWhiteSpace(password)) return Results.BadRequest("Sem senha RCON (nem no pool, nem no body).");

    using var rcon = new Summit.Api.RconClient();
    if (!await rcon.ConnectAndAuthAsync(body.Ip, 27015, password))
        return Results.BadRequest("Auth RCON falhou.");
    var result = await rcon.ExecCommandAsync(body.Command);
    return Results.Ok(new { result });
});

// diagnostico: regras de outbound do security group usado pelas EC2 do CS2
// (as de inbound já estao documentadas em docs/plano-aws.md; nunca conferimos as de saida)
app.MapGet("/api/debug/security-group", async () =>
{
    var sgId = Environment.GetEnvironmentVariable("SUMMIT_SECURITY_GROUP_ID");
    if (string.IsNullOrWhiteSpace(sgId)) return Results.Ok(new { configured = false });

    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeSecurityGroupsAsync(new Amazon.EC2.Model.DescribeSecurityGroupsRequest
    {
        GroupIds = new List<string> { sgId }
    });
    var sg = resp.SecurityGroups.FirstOrDefault();
    if (sg == null) return Results.Ok(new { configured = true, sgId, error = "grupo nao encontrado" });

    object Describe(List<Amazon.EC2.Model.IpPermission> perms) => perms.Select(p => new
    {
        protocol = p.IpProtocol,
        fromPort = p.FromPort,
        toPort = p.ToPort,
        cidrs = p.Ipv4Ranges.Select(r => r.CidrIp).ToList()
    });

    return Results.Ok(new
    {
        configured = true,
        sgId,
        inbound = Describe(sg.IpPermissions),
        outbound = Describe(sg.IpPermissionsEgress)
    });
});

// diagnostico: lista a chave (rounds + matches com id) sem mexer em nada — pra achar o
// bracketMatchId de uma partida sem precisar regenerar a chave (o que orfanaria o veto em curso).
app.MapGet("/api/debug/bracket/{tournamentId}", async (ApiDbContext db, string tournamentId) =>
{
    var rounds = await db.BracketRounds.Include(r => r.Matches)
        .Where(r => r.TournamentId == tournamentId).OrderBy(r => r.RoundNumber).ToListAsync();
    return Results.Ok(rounds.Select(r => new
    {
        r.Name,
        Side = r.Side.ToString(),
        r.RoundNumber,
        Matches = r.Matches.Select(m => new { m.Id, m.Position, m.TeamATag, m.TeamBTag, Status = m.Status.ToString() })
    }));
});

// dev: reabre o veto de uma partida do zero — apaga sessão/steps antigos e a sala (Match) que
// tinha ficado presa a um servidor já encerrado, volta o BracketMatch pra Pending e chama
// OpenVetoForMatchAsync de novo. Pra retestar sem precisar recriar o campeonato inteiro.
app.MapPost("/api/debug/restart-veto/{bracketMatchId}", async (ApiDbContext db, string bracketMatchId) =>
{
    var bm = await db.BracketMatches.Include(m => m.Round).FirstOrDefaultAsync(m => m.Id == bracketMatchId);
    if (bm == null) return Results.NotFound("BracketMatch não encontrado.");

    var oldMatches = await db.Matches.Where(m => m.BracketMatchId == bracketMatchId).ToListAsync();
    db.Matches.RemoveRange(oldMatches);

    var oldSession = await db.VetoSessions.Include(s => s.Steps).FirstOrDefaultAsync(s => s.BracketMatchId == bracketMatchId);
    if (oldSession != null)
    {
        db.VetoSteps.RemoveRange(oldSession.Steps);
        db.VetoSessions.Remove(oldSession);
    }

    bm.Status = Summit.Models.BracketMatchStatus.Pending;
    bm.ScoreA = null;
    bm.ScoreB = null;
    bm.MatchId = null;
    await db.SaveChangesAsync();

    await CompetitionEndpoints.OpenVetoForMatchAsync(db, bm.Round?.TournamentId, bm);
    await db.SaveChangesAsync();

    return Results.Ok(new { bracketMatchId, restarted = true });
});

// diagnostico: gera a chave na hora, sem esperar T-30min/T-0 (teste do bracket flexível/dupla elim.)
app.MapPost("/api/debug/generate-bracket/{tournamentId}", async (ApiDbContext db, string tournamentId) =>
{
    var t = await db.Tournaments.FirstOrDefaultAsync(x => x.Id == tournamentId);
    if (t == null) return Results.NotFound();

    var oldRounds = await db.BracketRounds.Where(r => r.TournamentId == tournamentId).ToListAsync();
    var oldMatchIds = oldRounds.Select(r => r.Id).ToList();
    db.BracketMatches.RemoveRange(db.BracketMatches.Where(m => oldMatchIds.Contains(m.RoundId)));
    db.BracketRounds.RemoveRange(oldRounds);

    var teams = await db.TournamentTeams.Include(tt => tt.Team)
        .Where(tt => tt.TournamentId == tournamentId && tt.Team != null)
        .OrderBy(tt => tt.Seed)
        .ToListAsync();
    if (teams.Count < 2) return Results.BadRequest("Precisa de pelo menos 2 times inscritos.");

    await LifecycleWorker.GenerateBracket(db, t, teams);
    await db.SaveChangesAsync();

    var rounds = await db.BracketRounds.Include(r => r.Matches)
        .Where(r => r.TournamentId == tournamentId).OrderBy(r => r.RoundNumber).ToListAsync();
    return Results.Ok(rounds.Select(r => new
    {
        r.Name,
        Side = r.Side.ToString(),
        r.RoundNumber,
        Matches = r.Matches.Select(m => new { m.Position, m.TeamATag, m.TeamBTag })
    }));
});

// diagnostico: sobe UMA instância pura da AMI, sem User Data nenhum — pra teste manual via SSH
app.MapPost("/api/debug/launch-bare-instance", async (MatchServerService server) =>
{
    if (!MatchServerService.IsConfigured) return Results.BadRequest("AWS não configurada.");
    var instanceId = await server.LaunchBareInstanceAsync();
    return Results.Ok(new { instanceId });
});

// dev (rebuild da AMI, docs/plano-aws.md Fase 3): acha a AMI Ubuntu oficial (Canonical) mais
// recente na região configurada — pra não ter que caçar o id manualmente no console.
app.MapGet("/api/debug/find-ubuntu-ami", async (string? release) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var namePattern = $"ubuntu/images/hvm-ssd-gp3/ubuntu-{release ?? "noble"}-*-amd64-server-*";
    var resp = await ec2.DescribeImagesAsync(new Amazon.EC2.Model.DescribeImagesRequest
    {
        Owners = new List<string> { "099720109477" }, // Canonical
        Filters = new List<Amazon.EC2.Model.Filter>
        {
            new() { Name = "name", Values = new List<string> { namePattern } },
            new() { Name = "state", Values = new List<string> { "available" } }
        }
    });
    var newest = resp.Images.OrderByDescending(i => i.CreationDate).FirstOrDefault();
    return newest == null
        ? Results.NotFound("Nenhuma AMI Ubuntu encontrada com esse filtro.")
        : Results.Ok(new { newest.ImageId, newest.Name, newest.CreationDate });
});

// dev (rebuild da AMI): sobe UMA instância a partir de uma AMI explícita (não a SUMMIT_AMI_ID,
// que pode estar quebrada/ausente) — usado pra recomeçar o build do zero numa Ubuntu limpa.
// Disco maior que o padrão porque o plano-aws.md documentou 60GiB como insuficiente pro CS2.
app.MapPost("/api/debug/launch-build-instance/{amiId}", async (string amiId, int? volumeGb) =>
{
    var securityGroupId = Environment.GetEnvironmentVariable("SUMMIT_SECURITY_GROUP_ID")
        ?? throw new InvalidOperationException("SUMMIT_SECURITY_GROUP_ID não configurada.");
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));

    var request = new Amazon.EC2.Model.RunInstancesRequest
    {
        ImageId = amiId,
        InstanceType = Amazon.EC2.InstanceType.C5Large,
        MinCount = 1,
        MaxCount = 1,
        KeyName = Environment.GetEnvironmentVariable("SUMMIT_KEY_PAIR_NAME"),
        SecurityGroupIds = new List<string> { securityGroupId },
        InstanceInitiatedShutdownBehavior = Amazon.EC2.ShutdownBehavior.Stop,
        BlockDeviceMappings = new List<Amazon.EC2.Model.BlockDeviceMapping>
        {
            new()
            {
                DeviceName = "/dev/sda1",
                Ebs = new Amazon.EC2.Model.EbsBlockDevice { VolumeSize = volumeGb ?? 100, VolumeType = Amazon.EC2.VolumeType.Gp3 }
            }
        },
        TagSpecifications = new List<Amazon.EC2.Model.TagSpecification>
        {
            new()
            {
                ResourceType = Amazon.EC2.ResourceType.Instance,
                Tags = new List<Amazon.EC2.Model.Tag> { new() { Key = "Name", Value = "summit-ami-build" }, new() { Key = "summit:manual", Value = "true" } }
            }
        }
    };
    var subnetId = Environment.GetEnvironmentVariable("SUMMIT_SUBNET_ID");
    if (!string.IsNullOrWhiteSpace(subnetId)) request.SubnetId = subnetId;

    var response = await ec2.RunInstancesAsync(request);
    return Results.Ok(new { instanceId = response.Reservation.Instances.First().InstanceId });
});

// infra (App Runner descontinuado -> EC2 direto pra hospedar a API): cria a IAM role + instance
// profile pra API rodar sem colar chaves AWS estáticas dentro do servidor. Policy gerenciada
// EC2FullAccess por ora (mesmo escopo que as chaves pessoais já tinham) — apertar depois.
app.MapPost("/api/debug/create-api-instance-role", async () =>
{
    using var iam = new Amazon.IdentityManagement.AmazonIdentityManagementServiceClient(Amazon.RegionEndpoint.USEast1);
    const string roleName = "summit-api-role";
    const string profileName = "summit-api-profile";
    const string trustPolicy = """
    {
      "Version": "2012-10-17",
      "Statement": [{ "Effect": "Allow", "Principal": { "Service": "ec2.amazonaws.com" }, "Action": "sts:AssumeRole" }]
    }
    """;

    try
    {
        await iam.CreateRoleAsync(new Amazon.IdentityManagement.Model.CreateRoleRequest
        {
            RoleName = roleName,
            AssumeRolePolicyDocument = trustPolicy
        });
    }
    catch (Amazon.IdentityManagement.Model.EntityAlreadyExistsException) { }

    await iam.AttachRolePolicyAsync(new Amazon.IdentityManagement.Model.AttachRolePolicyRequest
    {
        RoleName = roleName,
        PolicyArn = "arn:aws:iam::aws:policy/AmazonEC2FullAccess"
    });

    var createdNew = true;
    try
    {
        await iam.CreateInstanceProfileAsync(new Amazon.IdentityManagement.Model.CreateInstanceProfileRequest
        {
            InstanceProfileName = profileName
        });
    }
    catch (Amazon.IdentityManagement.Model.EntityAlreadyExistsException) { createdNew = false; }

    if (createdNew)
    {
        await iam.AddRoleToInstanceProfileAsync(new Amazon.IdentityManagement.Model.AddRoleToInstanceProfileRequest
        {
            InstanceProfileName = profileName,
            RoleName = roleName
        });
        // instance profiles novos levam alguns segundos pra propagar antes de poderem ser usados no RunInstances
        await Task.Delay(10000);
    }

    return Results.Ok(new { roleName, profileName, createdNew });
});

// infra: cria um security group dedicado (reusa a VPC do SG do CS2 já existente)
app.MapPost("/api/debug/create-security-group/{name}", async (string name, string? description) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));

    var existingSgId = Environment.GetEnvironmentVariable("SUMMIT_SECURITY_GROUP_ID")
        ?? throw new InvalidOperationException("SUMMIT_SECURITY_GROUP_ID não configurada.");
    var vpcResp = await ec2.DescribeSecurityGroupsAsync(new Amazon.EC2.Model.DescribeSecurityGroupsRequest
    {
        GroupIds = new List<string> { existingSgId }
    });
    var vpcId = vpcResp.SecurityGroups.First().VpcId;

    try
    {
        var resp = await ec2.CreateSecurityGroupAsync(new Amazon.EC2.Model.CreateSecurityGroupRequest
        {
            GroupName = name,
            Description = description ?? $"Summit - {name}",
            VpcId = vpcId
        });
        return Results.Ok(new { groupId = resp.GroupId, vpcId, existed = false });
    }
    catch (Amazon.EC2.AmazonEC2Exception ex) when (ex.ErrorCode == "InvalidGroup.Duplicate")
    {
        var existing = await ec2.DescribeSecurityGroupsAsync(new Amazon.EC2.Model.DescribeSecurityGroupsRequest
        {
            Filters = new List<Amazon.EC2.Model.Filter> { new() { Name = "group-name", Values = new List<string> { name } } }
        });
        return Results.Ok(new { groupId = existing.SecurityGroups.First().GroupId, vpcId, existed = true });
    }
});

// infra: sobe a instância que vai hospedar a Summit.Api de verdade (t3.micro — leve, barata,
// suficiente pro estágio atual). UserData só prepara o SO (.NET SDK + git); o deploy de
// verdade (clone do repo privado + env vars de produção) é feito depois via SSH.
app.MapPost("/api/debug/launch-api-instance/{amiId}", async (string amiId, string sgId, string? instanceProfile) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));

    const string userData = """
    #!/bin/bash
    set -e
    apt-get update -y
    apt-get install -y dotnet-sdk-8.0 git
    mkdir -p /opt/summit-api
    chown ubuntu:ubuntu /opt/summit-api
    """;

    var request = new Amazon.EC2.Model.RunInstancesRequest
    {
        ImageId = amiId,
        InstanceType = Amazon.EC2.InstanceType.T3Micro,
        MinCount = 1,
        MaxCount = 1,
        KeyName = Environment.GetEnvironmentVariable("SUMMIT_KEY_PAIR_NAME"),
        SecurityGroupIds = new List<string> { sgId },
        UserData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userData)),
        TagSpecifications = new List<Amazon.EC2.Model.TagSpecification>
        {
            new()
            {
                ResourceType = Amazon.EC2.ResourceType.Instance,
                Tags = new List<Amazon.EC2.Model.Tag> { new() { Key = "Name", Value = "summit-api-1" }, new() { Key = "summit:apihost", Value = "true" } }
            }
        }
    };
    // sem role IAM própria por ora (usuário summit-api não tem permissão de criar roles, de
    // propósito — ver memória do projeto); a instância usa as chaves estáticas via env var em
    // vez de instance profile. Trocar assim que a role for criada manualmente no console.
    if (!string.IsNullOrWhiteSpace(instanceProfile))
        request.IamInstanceProfile = new Amazon.EC2.Model.IamInstanceProfileSpecification { Name = instanceProfile };
    var subnetId = Environment.GetEnvironmentVariable("SUMMIT_SUBNET_ID");
    if (!string.IsNullOrWhiteSpace(subnetId)) request.SubnetId = subnetId;

    var response = await ec2.RunInstancesAsync(request);
    return Results.Ok(new { instanceId = response.Reservation.Instances.First().InstanceId });
});

// dev (rebuild da AMI): cria a AMI a partir de uma instância já configurada (CS2+MatchZy+CSS
// instalados e validados) — passo final da Fase 3 do plano-aws.md.
app.MapPost("/api/debug/create-image/{instanceId}", async (string instanceId, string name) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.CreateImageAsync(new Amazon.EC2.Model.CreateImageRequest
    {
        InstanceId = instanceId,
        Name = name,
        Description = $"Summit CS2 AMI - criada {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"
    });
    return Results.Ok(new { imageId = resp.ImageId });
});

// dev: cria um key pair novo (o SUMMIT_KEY_PAIR_NAME antigo pode ter sido apagado do console) —
// devolve o material da chave privada UMA vez só (a AWS não guarda cópia depois).
app.MapPost("/api/debug/create-key-pair/{name}", async (string name) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.CreateKeyPairAsync(new Amazon.EC2.Model.CreateKeyPairRequest { KeyName = name });
    return Results.Ok(new { keyName = resp.KeyPair.KeyName, keyMaterial = resp.KeyPair.KeyMaterial });
});

// dev: libera um CIDR novo pra SSH(22)/RCON(27015 tcp) no security group do CS2 — o IP de quem
// testa muda entre sessões, então a regra antiga pode estar apontando pra um IP morto.
// sgId/ports opcionais permitem reusar pra outros security groups (ex: RDS na porta 3306).
app.MapPost("/api/debug/authorize-ip/{cidr}", async (string cidr, string? sgId, string? ports)
    => // cidr vem como "1.2.3.4-32" pra fugir da barra na URL
{
    var parts = cidr.Split('-');
    var ip = $"{parts[0]}/{(parts.Length > 1 ? parts[1] : "32")}";
    var targetSgId = sgId ?? Environment.GetEnvironmentVariable("SUMMIT_SECURITY_GROUP_ID")
        ?? throw new InvalidOperationException("SUMMIT_SECURITY_GROUP_ID não configurada.");
    var targetPorts = string.IsNullOrWhiteSpace(ports)
        ? new[] { 22, 27015 }
        : ports.Split(',').Select(int.Parse).ToArray();
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));

    foreach (var port in targetPorts)
    {
        try
        {
            await ec2.AuthorizeSecurityGroupIngressAsync(new Amazon.EC2.Model.AuthorizeSecurityGroupIngressRequest
            {
                GroupId = targetSgId,
                IpPermissions = new List<Amazon.EC2.Model.IpPermission>
                {
                    new()
                    {
                        IpProtocol = "tcp", FromPort = port, ToPort = port,
                        Ipv4Ranges = new List<Amazon.EC2.Model.IpRange> { new() { CidrIp = ip, Description = "summit-debug" } }
                    }
                }
            });
        }
        catch (Amazon.EC2.AmazonEC2Exception ex) when (ex.ErrorCode == "InvalidPermission.Duplicate") { }
    }
    return Results.Ok(new { authorized = ip, sgId = targetSgId, ports = targetPorts });
});

// dev: lista todos os security groups da conta (id + nome) — usado pra achar o "default"
// que o RDS ficou usando, sem precisar abrir o console.
app.MapGet("/api/debug/security-groups", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeSecurityGroupsAsync(new Amazon.EC2.Model.DescribeSecurityGroupsRequest());
    return Results.Ok(resp.SecurityGroups.Select(sg => new { sg.GroupId, sg.GroupName, sg.VpcId }));
});

// diagnostico: volumes EBS (principalmente os "available" = órfãos, sem instância, só custando)
app.MapGet("/api/debug/volumes", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeVolumesAsync(new Amazon.EC2.Model.DescribeVolumesRequest());
    return Results.Ok(resp.Volumes.Select(v => new
    {
        v.VolumeId,
        v.Size,
        State = v.State.Value,
        v.CreateTime,
        AttachedTo = v.Attachments.Select(a => a.InstanceId).ToList(),
        Orphan = v.Attachments.Count == 0
    }));
});

// diagnostico: snapshots EBS próprios (a AMI tem um por trás que fica cobrando enquanto existir)
app.MapGet("/api/debug/snapshots", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeSnapshotsAsync(new Amazon.EC2.Model.DescribeSnapshotsRequest
    {
        OwnerIds = new List<string> { "self" }
    });
    return Results.Ok(resp.Snapshots.Select(s => new
    {
        s.SnapshotId,
        s.VolumeSize,
        s.StartTime,
        s.Description,
        s.Progress,
        State = s.State.Value
    }));
});

// dev: check-in forçado de um time inteiro — ignora janela de horário e permissão (o endpoint
// real POST /api/tournaments/{id}/checkin continua exigindo os dois; este é só pra teste).
app.MapPost("/api/debug/force-checkin/{tournamentId}/{teamId}", async (ApiDbContext db, string tournamentId, string teamId) =>
{
    var tt = await db.TournamentTeams.FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.TeamId == teamId);
    if (tt == null) return Results.NotFound("Time não está inscrito nesse campeonato.");
    tt.CheckIn = CheckInStatus.Confirmed;
    tt.CheckedInAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { tournamentId, teamId, checkedIn = true });
});

// dev: inscreve um time ignorando a janela de 12h (o endpoint real POST /api/tournaments/{id}/register
// continua fechando sozinho — este é só pra montar cenário de teste com o camp já perto do início).
app.MapPost("/api/debug/force-register/{tournamentId}/{teamId}", async (ApiDbContext db, string tournamentId, string teamId) =>
{
    var exists = await db.TournamentTeams.AnyAsync(x => x.TournamentId == tournamentId && x.TeamId == teamId);
    if (exists) return Results.Ok(true);

    var team = await db.Teams.Include(x => x.Members).FirstOrDefaultAsync(x => x.Id == teamId);
    if (team == null) return Results.NotFound("Time não encontrado.");

    var count = await db.TournamentTeams.CountAsync(x => x.TournamentId == tournamentId);
    var required = Math.Min(5, team.Members.Count);
    if (required < 1) return Results.BadRequest("Time sem membros.");
    var playerIds = team.Members.OrderBy(m => m.TeamJoinedAt ?? DateTime.MaxValue).Take(required).Select(m => m.Id).ToList();
    var captainId = playerIds.Contains(team.CaptainId) ? team.CaptainId : playerIds.FirstOrDefault();

    var tt = new TournamentTeam
    {
        Id = $"tt_{Guid.NewGuid():N}",
        TournamentId = tournamentId,
        TeamId = teamId,
        Seed = count + 1,
        RegisteredAt = DateTime.UtcNow,
        CaptainUserId = captainId,
        CheckIn = CheckInStatus.Waiting
    };
    db.TournamentTeams.Add(tt);
    foreach (var pid in playerIds.Distinct())
        db.TournamentLineupPlayers.Add(new TournamentLineupPlayer
        {
            Id = $"lp_{Guid.NewGuid():N}",
            TournamentTeamId = tt.Id,
            UserId = pid
        });
    await db.SaveChangesAsync();
    return Results.Ok(new { tournamentId, teamId, registered = true });
});

// dev: joga o veto inteiro sozinho (escolhe mapa aleatório disponível a cada passo, na ordem
// certa de quem deveria agir) até completar — reusa os endpoints reais de veto por dentro, não
// duplica a regra de sequência/validação de turno.
app.MapPost("/api/debug/simulate-veto/{bracketMatchId}", async (string bracketMatchId) =>
{
    using var http = new HttpClient
    { BaseAddress = new Uri($"http://localhost:{Environment.GetEnvironmentVariable("PORT") ?? "5180"}") };

    var startResp = await http.PostAsync($"/api/veto/{bracketMatchId}/start", null);
    if (!startResp.IsSuccessStatusCode)
        return Results.BadRequest("Não deu pra iniciar o veto — os dois times dessa partida já estão definidos?");

    for (var i = 0; i < 20; i++) // teto de segurança, nenhuma série real passa disso
    {
        var stateResp = await http.GetAsync($"/api/veto/{bracketMatchId}");
        if (!stateResp.IsSuccessStatusCode) break;
        using var state = JsonDocument.Parse(await stateResp.Content.ReadAsStringAsync());
        var root = state.RootElement;
        if (root.GetProperty("session").GetProperty("isComplete").GetBoolean()) break;
        if (!root.TryGetProperty("next", out var next) || next.ValueKind == JsonValueKind.Null) break;

        var team = next.GetProperty("team").GetString();
        var remaining = root.GetProperty("remaining").EnumerateArray().Select(x => x.GetString()!).ToList();
        if (remaining.Count == 0) break;
        var map = remaining[Random.Shared.Next(remaining.Count)];

        await http.PostAsJsonAsync($"/api/veto/{bracketMatchId}/action", new { teamTag = team, map });
    }

    using var finalResp = await http.GetAsync($"/api/veto/{bracketMatchId}");
    using var finalState = JsonDocument.Parse(await finalResp.Content.ReadAsStringAsync());
    var complete = finalState.RootElement.GetProperty("session").GetProperty("isComplete").GetBoolean();
    return Results.Ok(new { bracketMatchId, complete });
});

// dev: reinicia o provisionamento de uma sala que ficou presa em Failed (ex: AMI errada na
// hora do veto, já corrigida) — tenta o pool de novo, senão cai pro provisionamento direto,
// igual ProvisionRoomAsync faz na hora que o veto fecha.
app.MapPost("/api/debug/retry-provision/{bracketMatchId}", async (ApiDbContext db, IMatchServerProvider provider, string bracketMatchId) =>
{
    var match = await db.Matches.Where(m => m.BracketMatchId == bracketMatchId)
        .OrderByDescending(m => m.GameNumber).FirstOrDefaultAsync();
    if (match == null) return Results.NotFound("Sala da partida ainda não existe — o veto já terminou?");

    match.ProvisionState = Summit.Models.ServerProvisionState.None;
    await db.SaveChangesAsync();

    var assignedFromPool = await provider.TryAssignFromPoolAsync(match.Id, match.Map, match.ServerPassword);
    if (!assignedFromPool)
        _ = provider.ProvisionAsync(match.Id);

    return Results.Ok(new { matchId = match.Id, assignedFromPool });
});

// dev: força o resultado de uma partida AGORA, sem esperar o delay do provider local — pra
// "bypassar" uma partida manualmente durante teste. winner é opcional ("A"|"B"); sem winner,
// sorteia. Só funciona com SUMMIT_MATCH_PROVIDER=local (padrão).
app.MapPost("/api/debug/force-match-result/{bracketMatchId}", async (ApiDbContext db, IMatchServerProvider provider, string bracketMatchId, SimulateResultBody? body) =>
{
    // numa série (MD3/MD5) o confronto pode já ter mapas anteriores Finished — o "atual" é
    // sempre o de maior GameNumber, senão isso pegaria um mapa antigo já fechado.
    var match = await db.Matches.Where(m => m.BracketMatchId == bracketMatchId)
        .OrderByDescending(m => m.GameNumber).FirstOrDefaultAsync();
    if (match == null) return Results.NotFound("Sala da partida ainda não existe — o veto já terminou?");
    if (match.Status == Summit.Models.MatchStatus.Finished) return Results.Ok(new { alreadyFinished = true });
    if (provider is not LocalSimulatedMatchServerProvider local)
        return Results.BadRequest("Só funciona com SUMMIT_MATCH_PROVIDER=local.");

    char? winner = body?.Winner == null ? null : (body.Winner.Trim().ToUpperInvariant() == "B" ? 'B' : 'A');
    await local.ForceResultNowAsync(match.Id, winner);
    return Results.Ok(new { matchId = match.Id, forced = true });
});

// dev: lista usuários reais (login Steam de verdade). Real e seed usam o MESMO prefixo
// "usr_" — a diferença é o resto do id: login real gera "usr_" + GUID (36 chars no total,
// ex. usr_3f9a...), seed usa um apelido curto (usr_ghost, usr_niko...). Distingue pelo tamanho.
app.MapGet("/api/debug/real-users", async (ApiDbContext db) =>
{
    var users = await db.Users.Where(u => u.Id.Length == 36).ToListAsync();
    return Results.Ok(users.Select(u => new { u.Id, u.SteamId, u.Nickname, u.TeamId }));
});

// dev: apaga TODO campeonato/time/jogador fake e monta 4 times fixos (Geucius/NAVI/Spirit/
// Vitality, 5 jogadores cada) pra teste manual do fluxo completo — preserva só o usuário real
// passado em captainUserId (vira capitão do Geucius).
app.MapPost("/api/debug/reset-test-teams/{captainUserId}", async (ApiDbContext db, string captainUserId) =>
{
    var captain = await db.Users.FindAsync(captainUserId);
    if (captain == null) return Results.NotFound($"captainUserId {captainUserId} não encontrado.");

    db.Notifications.RemoveRange(db.Notifications);
    db.Reports.RemoveRange(db.Reports);
    db.AuditLogs.RemoveRange(db.AuditLogs);
    db.VetoSteps.RemoveRange(db.VetoSteps);
    db.VetoSessions.RemoveRange(db.VetoSessions);
    db.MatchPlayers.RemoveRange(db.MatchPlayers);
    db.Matches.RemoveRange(db.Matches);
    db.BracketMatches.RemoveRange(db.BracketMatches);
    db.BracketRounds.RemoveRange(db.BracketRounds);
    db.TournamentLineupPlayers.RemoveRange(db.TournamentLineupPlayers);
    db.TournamentTeams.RemoveRange(db.TournamentTeams);
    db.Tournaments.RemoveRange(db.Tournaments);
    db.TeamJoinRequests.RemoveRange(db.TeamJoinRequests);
    db.TeamInvitations.RemoveRange(db.TeamInvitations);
    db.Friendships.RemoveRange(db.Friendships);
    db.UserBadges.RemoveRange(db.UserBadges);
    db.PoolServers.RemoveRange(db.PoolServers);
    await db.SaveChangesAsync();

    db.Teams.RemoveRange(db.Teams);
    await db.SaveChangesAsync();

    // só apaga os fakes de seed (id curto tipo "usr_ghost") — usuários reais (id = "usr_" +
    // GUID, 36 chars) ficam intactos mesmo que não sejam o captain.
    var fakeUsers = await db.Users.Where(u => u.Id.Length != 36).ToListAsync();
    db.Users.RemoveRange(fakeUsers);
    await db.SaveChangesAsync();

    var now = DateTime.UtcNow;
    User MakeUser(string id, string nick) => new()
    {
        Id = id, SteamId = "test_" + id, Nickname = nick, Country = "🇧🇷", Rank = "Global Elite",
        Level = 50, Elo = 2500, WinRate = 0.6, KD = 1.2, HeadshotPercent = 0.5, AvgDamagePerRound = 75,
        CreatedAt = now, LastLoginAt = now
    };
    Team MakeTeam(string id, string name, string tag, string capId) => new()
    { Id = id, Name = name, Tag = tag, CaptainId = capId, Elo = 2600, CreatedAt = now };

    var geuTeam = MakeTeam("team_geucius", "The Geucius", "GEUCIUS", captainUserId);
    captain.TeamId = geuTeam.Id;
    captain.TeamRole = TeamRole.Captain;
    var geuMembers = new[] { MakeUser("usr_divi", "divi"), MakeUser("usr_hiosyin", "hiosyin"), MakeUser("usr_jarro", "jarro"), MakeUser("usr_leandro", "leandro") };
    foreach (var u in geuMembers) { u.TeamId = geuTeam.Id; u.TeamRole = TeamRole.Member; }

    var naviTeam = MakeTeam("team_navi", "Natus Vincere", "NAVI", "usr_aleksib");
    var navi = new[] { MakeUser("usr_aleksib", "Aleksib"), MakeUser("usr_im", "iM"), MakeUser("usr_b1t", "b1t"), MakeUser("usr_wonderful", "w0nderful"), MakeUser("usr_makaze", "Makaze") };
    navi[0].TeamRole = TeamRole.Captain;
    foreach (var u in navi) u.TeamId = naviTeam.Id;

    var spiritTeam = MakeTeam("team_spirit", "Team Spirit", "SPIRIT", "usr_donk");
    var spirit = new[] { MakeUser("usr_donk", "donk"), MakeUser("usr_tn1r", "tn1r"), MakeUser("usr_magix", "magix"), MakeUser("usr_zontix", "zontix"), MakeUser("usr_sh1ro", "sh1ro") };
    spirit[0].TeamRole = TeamRole.Captain;
    foreach (var u in spirit) u.TeamId = spiritTeam.Id;

    var vitTeam = MakeTeam("team_vitality", "Vitality", "VITALITY", "usr_zywoo");
    var vit = new[] { MakeUser("usr_zywoo", "ZywOo"), MakeUser("usr_ropz", "ropz"), MakeUser("usr_apex", "apex"), MakeUser("usr_mezzi", "mezzi"), MakeUser("usr_flamez", "flamez") };
    vit[0].TeamRole = TeamRole.Captain;
    foreach (var u in vit) u.TeamId = vitTeam.Id;

    await db.Teams.AddRangeAsync(geuTeam, naviTeam, spiritTeam, vitTeam);
    await db.Users.AddRangeAsync(geuMembers);
    await db.Users.AddRangeAsync(navi);
    await db.Users.AddRangeAsync(spirit);
    await db.Users.AddRangeAsync(vit);
    await db.SaveChangesAsync();

    return Results.Ok(new { teams = new[] { geuTeam.Id, naviTeam.Id, spiritTeam.Id, vitTeam.Id } });
});

// dev: monta um campeonato de teste completo com os 4 times fixos do reset-test-teams —
// registra, confirma check-in dos 4 e já gera a chave + abre o veto da 1ª rodada, sem
// precisar esperar as janelas reais de inscrição/check-in/horário.
app.MapPost("/api/debug/create-test-tournament", async (ApiDbContext db) =>
{
    var teamIds = new[] { "team_geucius", "team_navi", "team_spirit", "team_vitality" };
    var teams = await db.Teams.Include(t => t.Members).Where(t => teamIds.Contains(t.Id)).ToListAsync();
    if (teams.Count != 4) return Results.BadRequest("Times de teste não encontrados — rode /api/debug/reset-test-teams primeiro.");

    var organizerId = teams.First(t => t.Id == "team_geucius").CaptainId;
    var now = DateTime.UtcNow;
    var tour = new Tournament
    {
        Id = $"trn_test_{Guid.NewGuid():N}"[..20],
        Name = "Summit Test Cup",
        Format = "5v5 • Eliminação Simples • MD1",
        Status = TournamentStatus.Open,
        Prize = "Teste",
        MaxTeams = 4,
        StartDate = now,
        Description = "Campeonato de teste gerado via debug.",
        Rules = "MD1.",
        Organizer = "Summit Staff",
        OrganizerUserId = organizerId,
        MapPoolCsv = "Mirage, Inferno, Nuke, Ancient, Anubis, Dust2, Vertigo",
        FormatType = TournamentFormat.SingleElimination,
        Series = SeriesFormat.MD1,
        FinalSeries = SeriesFormat.MD1
    };
    db.Tournaments.Add(tour);
    await db.SaveChangesAsync();

    var seed = 1;
    foreach (var team in teams.OrderBy(t => t.Id))
    {
        var required = Math.Min(5, team.Members.Count);
        var playerIds = team.Members.Take(required).Select(m => m.Id).ToList();
        var captainId = playerIds.Contains(team.CaptainId) ? team.CaptainId : playerIds.FirstOrDefault();
        var tt = new TournamentTeam
        {
            Id = $"tt_{Guid.NewGuid():N}",
            TournamentId = tour.Id,
            TeamId = team.Id,
            Seed = seed++,
            RegisteredAt = now,
            CaptainUserId = captainId,
            CheckIn = CheckInStatus.Confirmed,
            CheckedInAt = now
        };
        db.TournamentTeams.Add(tt);
        foreach (var pid in playerIds)
            db.TournamentLineupPlayers.Add(new TournamentLineupPlayer { Id = $"lp_{Guid.NewGuid():N}", TournamentTeamId = tt.Id, UserId = pid });
    }
    await db.SaveChangesAsync();

    var ttWithTeam = await db.TournamentTeams.Include(x => x.Team)
        .Where(x => x.TournamentId == tour.Id).OrderBy(x => x.Seed).ToListAsync();
    await LifecycleWorker.GenerateBracket(db, tour, ttWithTeam);
    tour.Status = TournamentStatus.InProgress;
    await db.SaveChangesAsync();

    var freshTour = await db.Tournaments.Include(t => t.Bracket).ThenInclude(r => r.Matches)
        .FirstAsync(t => t.Id == tour.Id);
    var r1 = freshTour.Bracket.OrderBy(r => r.RoundNumber).First();
    foreach (var bm in r1.Matches)
        await CompetitionEndpoints.OpenVetoForMatchAsync(db, tour.Id, bm);
    await db.SaveChangesAsync();

    return Results.Ok(new { tournamentId = tour.Id, name = tour.Name });
});

// dev: registra N times "fantasma" (com 5 jogadores fake cada, escalação e check-in já
// confirmados) direto num campeonato — pra testar chave/avanço sem precisar de times reais.
app.MapPost("/api/debug/add-ghost-teams/{tournamentId}", async (ApiDbContext db, string tournamentId, int? count) =>
{
    var t = await db.Tournaments.FindAsync(tournamentId);
    if (t == null) return Results.NotFound("Campeonato inexistente.");

    var existingSeed = await db.TournamentTeams.CountAsync(x => x.TournamentId == tournamentId);
    var slotsLeft = t.MaxTeams - existingSeed;
    if (slotsLeft <= 0) return Results.BadRequest("Campeonato já está com o máximo de times.");

    var n = Math.Clamp(count ?? 1, 1, Math.Min(16, slotsLeft));
    var created = new List<object>();

    for (var i = 0; i < n; i++)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var team = new Team
        {
            Id = $"team_ghost_{suffix}",
            Name = $"Ghost Team {suffix}",
            Tag = $"GH{suffix[..3]}".ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
            Elo = 1000 + Random.Shared.Next(-200, 200)
        };

        var members = new List<User>();
        for (var m = 0; m < 5; m++)
        {
            var user = new User
            {
                Id = $"usr_ghost_{suffix}_{m}",
                SteamId = $"765611980{Random.Shared.NextInt64(10000000, 99999999)}",
                Nickname = $"Fantasma_{suffix}_{m}",
                Rank = "Unranked",
                PrimaryRole = "Rifler",
                Level = 1,
                TeamId = team.Id,
                TeamRole = m == 0 ? TeamRole.Captain : TeamRole.Member,
                TeamJoinedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            members.Add(user);
        }
        team.CaptainId = members[0].Id;
        db.Teams.Add(team);

        var tt = new TournamentTeam
        {
            Id = $"tt_{Guid.NewGuid():N}",
            TournamentId = tournamentId,
            TeamId = team.Id,
            Seed = existingSeed + i + 1,
            RegisteredAt = DateTime.UtcNow,
            CaptainUserId = members[0].Id,
            CheckIn = CheckInStatus.Confirmed,
            CheckedInAt = DateTime.UtcNow
        };
        db.TournamentTeams.Add(tt);
        foreach (var m in members)
            db.TournamentLineupPlayers.Add(new TournamentLineupPlayer
            {
                Id = $"lp_{Guid.NewGuid():N}",
                TournamentTeamId = tt.Id,
                UserId = m.Id
            });

        created.Add(new { team.Id, team.Name, team.Tag });
    }

    await db.SaveChangesAsync();
    return Results.Ok(created);
});

// dev: muda a data de início do campeonato livremente — pra pular a espera de T-12h/T-1h/T-30min
// e disparar fechamento de check-in / geração de chave / início na hora que você quiser.
app.MapPost("/api/debug/set-tournament-date/{tournamentId}", async (ApiDbContext db, string tournamentId, SetTournamentDateBody body) =>
{
    var t = await db.Tournaments.FirstOrDefaultAsync(x => x.Id == tournamentId);
    if (t == null) return Results.NotFound();
    t.StartDate = body.StartDate;
    await db.SaveChangesAsync();
    return Results.Ok(new
    {
        t.Id,
        t.StartDate,
        t.RegistrationClosesAt,
        t.CheckInOpensAt,
        t.CheckInClosesAt
    });
});

// manutencao: desregistra uma AMI (precisa ser feito ANTES de apagar o snapshot por trás dela)
app.MapPost("/api/debug/deregister-ami/{amiId}", async (string amiId) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    await ec2.DeregisterImageAsync(new Amazon.EC2.Model.DeregisterImageRequest { ImageId = amiId });
    return Results.Ok(new { deregistered = amiId });
});

// manutencao: apaga um snapshot EBS (só funciona se nenhuma AMI ainda referenciar ele)
app.MapPost("/api/debug/delete-snapshot/{snapshotId}", async (string snapshotId) =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    await ec2.DeleteSnapshotAsync(new Amazon.EC2.Model.DeleteSnapshotRequest { SnapshotId = snapshotId });
    return Results.Ok(new { deleted = snapshotId });
});

// dev: força quem vence a próxima simulação de resultado de uma partida (provider local, RF-00) —
// só tem efeito com SUMMIT_MATCH_PROVIDER=local (padrão) e antes do delay automático disparar.
app.MapPost("/api/debug/simulate-result/{matchId}", (string matchId, SimulateResultBody body) =>
{
    var side = body.Winner.Trim().ToUpperInvariant() == "B" ? 'B' : 'A';
    LocalSimulatedMatchServerProvider.SetWinnerOverride(matchId, side);
    return Results.Ok(new { matchId, winner = side.ToString() });
});

// diagnostico: NAT Gateways ativos — cobram por hora só de existir, mesmo sem tráfego nenhum
app.MapGet("/api/debug/nat-gateways", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeNatGatewaysAsync(new Amazon.EC2.Model.DescribeNatGatewaysRequest());
    return Results.Ok(resp.NatGateways
        .Where(n => n.State != Amazon.EC2.NatGatewayState.Deleted)
        .Select(n => new
        {
            n.NatGatewayId,
            State = n.State.Value,
            n.VpcId,
            n.SubnetId,
            n.CreateTime,
            PublicIps = n.NatGatewayAddresses.Select(a => a.PublicIp).ToList()
        }));
});

// diagnostico: Elastic IPs — cobram por hora quando NÃO estão associados a uma instância rodando
app.MapGet("/api/debug/elastic-ips", async () =>
{
    var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "sa-east-1";
    using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(region));
    var resp = await ec2.DescribeAddressesAsync(new Amazon.EC2.Model.DescribeAddressesRequest());
    return Results.Ok(resp.Addresses.Select(a => new
    {
        a.PublicIp,
        a.AllocationId,
        a.InstanceId,
        a.AssociationId,
        Unassociated = string.IsNullOrEmpty(a.AssociationId)
    }));
});

// diagnostico: instâncias EC2 em TODAS as regiões habilitadas (o resto do /api/debug/* só olha
// a região configurada em AWS_REGION — isso cobre o ponto cego de algo criado em outra região)
app.MapGet("/api/debug/all-regions-instances", async () =>
{
    using var global = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.USEast1);
    var regionsResp = await global.DescribeRegionsAsync(new Amazon.EC2.Model.DescribeRegionsRequest());
    var found = new List<object>();
    foreach (var r in regionsResp.Regions)
    {
        try
        {
            using var ec2 = new Amazon.EC2.AmazonEC2Client(Amazon.RegionEndpoint.GetBySystemName(r.RegionName));
            var resp = await ec2.DescribeInstancesAsync(new Amazon.EC2.Model.DescribeInstancesRequest());
            foreach (var i in resp.Reservations.SelectMany(res => res.Instances))
            {
                if (i.State.Name == Amazon.EC2.InstanceStateName.Terminated) continue;
                found.Add(new
                {
                    region = r.RegionName,
                    instanceId = i.InstanceId,
                    state = i.State.Name.Value,
                    instanceType = i.InstanceType?.Value,
                    publicIp = i.PublicIpAddress
                });
            }
        }
        catch { /* região sem permissão/acesso — ignora e segue */ }
    }
    return Results.Ok(found);
});

// diagnostico: estado do pool de servidores quentes (docs/plano-aws.md)
app.MapGet("/api/debug/pool", async (ApiDbContext db) =>
{
    var pool = await db.PoolServers.OrderBy(p => p.CreatedAt).ToListAsync();
    return Results.Ok(pool.Select(p => new
    {
        p.Id,
        p.Ec2InstanceId,
        p.PublicIp,
        state = p.State.ToString(),
        p.CurrentMatchId,
        p.AssignedAt,
        p.CreatedAt
    }));
});

} // enableDebugEndpoints

// ═════════════════════════════ USERS ═════════════════════════════

app.MapPost("/api/users/steam-login", async (ApiDbContext db, SteamLoginRequest req) =>
{
    var existing = await db.Users.Include(u => u.Team)
        .FirstOrDefaultAsync(u => u.SteamId == req.SteamId);

    User user;
    if (existing == null)
    {
        user = new User
        {
            Id = $"usr_{Guid.NewGuid():N}",
            SteamId = req.SteamId,
            Nickname = req.Nickname,
            AvatarUrl = req.AvatarUrl,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            Rank = "Unranked",
            PrimaryRole = "Rifler",
            Bio = string.Empty,
            FavoriteMap = string.Empty,
            TeamId = null,
            Level = 1
        };
        db.Users.Add(user);
    }
    else
    {
        if (!string.IsNullOrWhiteSpace(req.Nickname))
            existing.Nickname = req.Nickname;
        if (!string.IsNullOrWhiteSpace(req.AvatarUrl))
            existing.AvatarUrl = req.AvatarUrl;
        existing.LastLoginAt = DateTime.UtcNow;
        user = existing;
    }
    await db.SaveChangesAsync();

    // token emitido aqui — o handshake OpenID com a Steam já foi verificado de verdade
    // pelo client (SteamAuthService.ValidateWithSteamAsync) antes de chegar até aqui.
    var token = SummitAuth.GenerateToken(user.Id);
    return Results.Ok(new AuthResult { User = user, Token = token });
});

app.MapGet("/api/users/{id}", async (ApiDbContext db, string id) =>
{
    var u = await db.Users.Include(x => x.Team).ThenInclude(t => t!.Members)
        .FirstOrDefaultAsync(x => x.Id == id);
    return u == null ? Results.NotFound() : Results.Ok(u);
});

// restauro de sessão do client — lê o id do token, não confia em nenhum steamId enviado à toa.
app.MapGet("/api/users/me", async (ApiDbContext db, HttpContext ctx) =>
{
    var userId = SummitAuth.GetUserId(ctx);
    if (userId == null) return Results.Unauthorized();
    var u = await db.Users.Include(x => x.Team).ThenInclude(t => t!.Members)
        .FirstOrDefaultAsync(x => x.Id == userId);
    return u == null ? Results.NotFound() : Results.Ok(u);
}).RequireAuthorization();

app.MapGet("/api/users/by-steam/{steamId}", async (ApiDbContext db, string steamId) =>
{
    var u = await db.Users.Include(x => x.Team).ThenInclude(t => t!.Members)
        .FirstOrDefaultAsync(x => x.SteamId == steamId);
    return u == null ? Results.NotFound() : Results.Ok(u);
});

app.MapGet("/api/users/by-nickname/{nickname}", async (ApiDbContext db, string nickname) =>
{
    var u = await db.Users
        .FirstOrDefaultAsync(x => x.Nickname.ToLower() == nickname.ToLower());
    return u == null ? Results.NotFound() : Results.Ok(u);
});

app.MapGet("/api/users/search", async (ApiDbContext db, string? q) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new List<User>());
    var query = q.ToLower();
    var list = await db.Users
        .Where(u => u.Nickname.ToLower().Contains(query))
        .OrderBy(u => u.Nickname)
        .Take(20)
        .ToListAsync();
    return Results.Ok(list);
});

app.MapPut("/api/users/{id}", async (ApiDbContext db, HttpContext ctx, string id, User body) =>
{
    if (SummitAuth.GetUserId(ctx) != id) return Results.Forbid();
    var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (existing == null) return Results.NotFound();

    existing.Nickname          = body.Nickname;
    existing.AvatarUrl         = body.AvatarUrl;
    existing.Bio               = body.Bio;
    existing.PrimaryRole       = body.PrimaryRole;
    existing.Rank              = body.Rank;
    existing.Level             = body.Level;
    existing.WinRate           = body.WinRate;
    existing.KD                = body.KD;
    existing.HeadshotPercent   = body.HeadshotPercent;
    existing.AvgDamagePerRound = body.AvgDamagePerRound;
    existing.TotalMatches      = body.TotalMatches;
    existing.TotalWins         = body.TotalWins;
    existing.TotalKills        = body.TotalKills;
    existing.TotalDeaths       = body.TotalDeaths;
    existing.TotalAssists      = body.TotalAssists;
    existing.Elo               = body.Elo;
    existing.FavoriteMap       = body.FavoriteMap;
    existing.FavoriteWeapon    = body.FavoriteWeapon;
    existing.Country           = body.Country;
    existing.TeamId            = body.TeamId;
    existing.TeamRole          = body.TeamRole;
    existing.TeamJoinedAt      = body.TeamJoinedAt;
    existing.LastLoginAt       = body.LastLoginAt;
    await db.SaveChangesAsync();
    return Results.Ok(existing);
}).RequireAuthorization();

// ═════════════════════════════ TEAMS ═════════════════════════════

app.MapGet("/api/teams", async (ApiDbContext db) =>
    Results.Ok(await db.Teams.Include(t => t.Members).OrderByDescending(t => t.Elo).ToListAsync()));

app.MapGet("/api/teams/{id}", async (ApiDbContext db, string id) =>
{
    var t = await db.Teams.Include(x => x.Members).FirstOrDefaultAsync(x => x.Id == id);
    return t == null ? Results.NotFound() : Results.Ok(t);
});

app.MapGet("/api/teams/by-tag/{tag}", async (ApiDbContext db, string tag) =>
{
    var t = await db.Teams.Include(x => x.Members)
        .FirstOrDefaultAsync(x => x.Tag.ToLower() == tag.ToLower());
    return t == null ? Results.NotFound() : Results.Ok(t);
});

app.MapPost("/api/teams", async (ApiDbContext db, HttpContext ctx, CreateTeamRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var team = new Team
    {
        Id = $"team_{Guid.NewGuid():N}",
        Name = req.Name,
        Tag = req.Tag,
        CaptainId = authUserId,
        CreatedAt = DateTime.UtcNow
    };
    db.Teams.Add(team);

    var captain = await db.Users.FirstOrDefaultAsync(u => u.Id == authUserId);
    if (captain != null)
    {
        captain.TeamId = team.Id;
        captain.TeamRole = TeamRole.Captain;
        captain.TeamJoinedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return Results.Ok(team);
}).RequireAuthorization();

app.MapPut("/api/teams/{id}", async (ApiDbContext db, HttpContext ctx, string id, UpdateTeamRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    if (!await CompetitionEndpoints.IsOwner(db, id, authUserId)) return Results.Forbid();
    var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id);
    if (team == null) return Results.NotFound();

    team.Name = req.Name;
    team.Description = req.Description ?? "";
    team.LogoUrl = req.LogoUrl ?? "";
    team.Country = req.Country ?? "";
    await CompetitionEndpoints.Audit(db, "team_edited", authUserId, null, id, null, null, req.Name, null);
    await db.SaveChangesAsync();
    return Results.Ok(team);
}).RequireAuthorization();

app.MapDelete("/api/teams/{id}", async (ApiDbContext db, HttpContext ctx, string id) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!; // RequireAuthorization() garante não-nulo
    if (!await CompetitionEndpoints.IsOwner(db, id, authUserId)) return Results.Forbid();
    var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id);
    if (team == null) return Results.NotFound();

    // Fase 11 (docs/spec/summit-fase-final/tasks.md): não deixa apagar um time que ainda está
    // disputando algo — inscrito e não eliminado num campeonato que não terminou.
    var activeTournamentName = await db.TournamentTeams.Include(tt => tt.Tournament)
        .Where(tt => tt.TeamId == id && !tt.IsEliminated
                  && tt.Tournament != null && tt.Tournament.Status != TournamentStatus.Finished)
        .Select(tt => tt.Tournament!.Name)
        .FirstOrDefaultAsync();
    if (activeTournamentName != null)
        return Results.BadRequest($"Não é possível excluir o time: ele está inscrito no campeonato ativo \"{activeTournamentName}\".");

    var members = await db.Users.Where(u => u.TeamId == id).ToListAsync();
    foreach (var m in members)
    {
        m.TeamId = null;
        m.TeamRole = TeamRole.Member;
        m.TeamJoinedAt = null;
    }
    db.Teams.Remove(team);
    await CompetitionEndpoints.Audit(db, "team_deleted", authUserId, null, id, null, team.Name, null, null);
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapPost("/api/teams/{teamId}/kick", async (ApiDbContext db, HttpContext ctx, string teamId, RoleBody body) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    if (!await CompetitionEndpoints.IsOwner(db, teamId, authUserId)) return Results.Forbid();
    if (body.UserId == authUserId) return Results.BadRequest("O dono não pode remover a si mesmo.");

    var target = await db.Users.FirstOrDefaultAsync(u => u.Id == body.UserId && u.TeamId == teamId);
    if (target == null) return Results.NotFound();

    target.TeamId = null;
    target.TeamRole = TeamRole.Member;
    target.TeamJoinedAt = null;
    await CompetitionEndpoints.Audit(db, "member_kicked", authUserId, body.UserId, teamId, null, null, null, null);
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

// só as próprias — o userId da rota é ignorado, sempre lê do token (privacidade: convite é dado pessoal).
app.MapGet("/api/teams/invitations/{userId}", async (ApiDbContext db, HttpContext ctx, string userId) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    return Results.Ok(await db.TeamInvitations
        .Include(i => i.Team).ThenInclude(t => t!.Members)
        .Include(i => i.InvitedBy)
        .Where(i => i.InvitedUserId == authUserId && i.Status == TeamInvitationStatus.Pending)
        .OrderByDescending(i => i.CreatedAt)
        .ToListAsync());
}).RequireAuthorization();

app.MapPost("/api/teams/{teamId}/invite", async (ApiDbContext db, HttpContext ctx, string teamId, InviteRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!; // RequireAuthorization() garante não-nulo
    var inviter = await db.Users.FirstOrDefaultAsync(u => u.Id == authUserId);
    if (inviter == null || inviter.TeamId != teamId) return Results.BadRequest("Você não faz parte desse time.");
    // somente o DONO convida jogadores (espec-times §3.1/§7)
    if (inviter.TeamRole != TeamRole.Captain)
        return Results.BadRequest("Só o capitão do time pode convidar jogadores.");

    var target = await db.Users.FirstOrDefaultAsync(u => u.Id == req.InvitedUserId);
    if (target == null) return Results.BadRequest("Jogador não encontrado.");
    if (target.TeamId != null) return Results.BadRequest("Esse jogador já está em um time.");

    var existing = await db.TeamInvitations
        .FirstOrDefaultAsync(i => i.TeamId == teamId
                               && i.InvitedUserId == req.InvitedUserId
                               && i.Status == TeamInvitationStatus.Pending);
    if (existing != null) return Results.Ok(existing);

    var inv = new TeamInvitation
    {
        Id = $"inv_{Guid.NewGuid():N}",
        TeamId = teamId,
        InvitedUserId = req.InvitedUserId,
        InvitedById = authUserId,
        Status = TeamInvitationStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };
    db.TeamInvitations.Add(inv);

    var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
    await NotificationHelper.Notify(db, req.InvitedUserId, NotificationType.TeamInvite,
        $"Você recebeu um convite pra entrar no time {team?.Name ?? teamId}.", teamId);

    await db.SaveChangesAsync();
    return Results.Ok(inv);
}).RequireAuthorization();

app.MapPost("/api/teams/invitations/{id}/accept", async (ApiDbContext db, HttpContext ctx, string id) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var inv = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == id);
    if (inv == null || inv.Status != TeamInvitationStatus.Pending) return Results.BadRequest();
    if (inv.InvitedUserId != authUserId) return Results.Forbid(); // só quem foi convidado aceita

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == inv.InvitedUserId);
    if (user == null || user.TeamId != null) return Results.BadRequest();

    user.TeamId = inv.TeamId;
    user.TeamRole = TeamRole.Member;
    user.TeamJoinedAt = DateTime.UtcNow;
    inv.Status = TeamInvitationStatus.Accepted;
    inv.RespondedAt = DateTime.UtcNow;

    var others = await db.TeamInvitations
        .Where(i => i.InvitedUserId == inv.InvitedUserId
                 && i.Status == TeamInvitationStatus.Pending
                 && i.Id != id)
        .ToListAsync();
    foreach (var o in others)
    {
        o.Status = TeamInvitationStatus.Cancelled;
        o.RespondedAt = DateTime.UtcNow;
    }

    await CompetitionEndpoints.Audit(db, "invite_accepted", user.Id, null, inv.TeamId, null, null, null, null);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/api/teams/invitations/{id}/decline", async (ApiDbContext db, HttpContext ctx, string id) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var inv = await db.TeamInvitations.FirstOrDefaultAsync(i => i.Id == id);
    if (inv == null || inv.Status != TeamInvitationStatus.Pending) return Results.BadRequest();
    if (inv.InvitedUserId != authUserId) return Results.Forbid(); // só quem foi convidado recusa
    inv.Status = TeamInvitationStatus.Declined;
    inv.RespondedAt = DateTime.UtcNow;
    await CompetitionEndpoints.Audit(db, "invite_declined", inv.InvitedUserId, null, inv.TeamId, null, null, null, null);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

// Saída do time — com transferência automática de propriedade (espec-times §12-13)
app.MapPost("/api/teams/leave/{userId}", async (ApiDbContext db, HttpContext ctx, string userId) =>
{
    if (SummitAuth.GetUserId(ctx) != userId) return Results.Forbid(); // ninguém sai do time por outra pessoa
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    if (user == null || user.TeamId == null) return Results.BadRequest();
    var teamId = user.TeamId;

    if (user.TeamRole == TeamRole.Captain)
    {
        var others = await db.Users
            .Where(u => u.TeamId == teamId && u.Id != userId)
            .ToListAsync();

        if (others.Count == 0)
        {
            // último membro: o time é excluído (§13)
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            if (team != null) db.Teams.Remove(team);
            await CompetitionEndpoints.Audit(db, "team_deleted", userId, null, teamId, null, null, null,
                "Dono saiu e o time não possuía outros membros");
        }
        else
        {
            // ordem: sublíder mais antigo → membro mais antigo → id (§12)
            var newOwner = others
                .OrderByDescending(u => u.TeamRole == TeamRole.ViceCaptain)
                .ThenBy(u => u.TeamJoinedAt ?? DateTime.MaxValue)
                .ThenBy(u => u.Id)
                .First();
            newOwner.TeamRole = TeamRole.Captain;
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
            if (team != null) team.CaptainId = newOwner.Id;
            await CompetitionEndpoints.Audit(db, "ownership_auto_transferred", userId, newOwner.Id,
                teamId, null, user.Nickname, newOwner.Nickname, "Saída do dono");
        }
    }

    user.TeamId = null;
    user.TeamRole = TeamRole.Member;
    user.TeamJoinedAt = null;
    await CompetitionEndpoints.Audit(db, "member_left", userId, null, teamId, null, null, null, null);
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

// ═════════════════════════════ TOURNAMENTS ═════════════════════════════

app.MapGet("/api/tournaments", async (ApiDbContext db) =>
    Results.Ok(await db.Tournaments
        .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
        .OrderBy(t => t.Status)
        .ThenBy(t => t.StartDate)
        .ToListAsync()));

app.MapGet("/api/tournaments/{id}", async (ApiDbContext db, string id) =>
{
    var t = await db.Tournaments
        .Include(x => x.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
        .Include(x => x.Bracket).ThenInclude(r => r.Matches)
        .FirstOrDefaultAsync(x => x.Id == id);
    return t == null ? Results.NotFound() : Results.Ok(t);
});

// Criação de campeonato pelo organizador (docs/spec/summit-fase-final/plan.md RF-09)
app.MapPost("/api/tournaments", (ApiDbContext db, HttpContext ctx, CreateTournamentRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    if (req.StartDate <= DateTime.UtcNow) return Results.BadRequest("A data de início precisa ser no futuro.");
    if (req.MinTeams < 2) return Results.BadRequest("Mínimo de times precisa ser pelo menos 2.");
    if (req.MinTeams > req.MaxTeams) return Results.BadRequest("Mínimo de times não pode ser maior que o máximo.");
    var maps = (req.MapPoolCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (maps.Length < 3) return Results.BadRequest("O map pool precisa ter pelo menos 3 mapas.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Nome é obrigatório.");

    var t = new Tournament
    {
        Id = $"trn_{Guid.NewGuid():N}",
        Name = req.Name,
        Description = req.Description ?? "",
        Region = string.IsNullOrWhiteSpace(req.Region) ? "América do Sul" : req.Region,
        StartDate = req.StartDate,
        FormatType = req.FormatType,
        Series = req.Series,
        FinalSeries = req.FinalSeries,
        MapPoolCsv = req.MapPoolCsv,
        MinTeams = req.MinTeams,
        MaxTeams = req.MaxTeams,
        Prize = req.Prize ?? "",
        IsPaidEntry = req.IsPaidEntry,
        EntryFee = req.EntryFee ?? "",
        OrganizerUserId = authUserId,
        Organizer = req.OrganizerName ?? authUserId,
        Status = TournamentStatus.Open,
        Game = "CS2"
    };
    db.Tournaments.Add(t);
    db.SaveChanges();
    return Results.Ok(t);
}).RequireAuthorization();

// Edição — só o organizador, só antes do fechamento de inscrições (RF-09)
app.MapPut("/api/tournaments/{id}", async (ApiDbContext db, HttpContext ctx, string id, UpdateTournamentRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var t = await db.Tournaments.FirstOrDefaultAsync(x => x.Id == id);
    if (t == null) return Results.NotFound();
    if (t.OrganizerUserId != authUserId) return Results.Forbid();
    if (DateTime.UtcNow >= t.RegistrationClosesAt)
        return Results.BadRequest("Não é possível editar: as inscrições já fecharam.");

    t.Name = req.Name;
    t.Description = req.Description ?? "";
    t.Region = req.Region ?? t.Region;
    t.StartDate = req.StartDate;
    t.FormatType = req.FormatType;
    t.Series = req.Series;
    t.FinalSeries = req.FinalSeries;
    t.MapPoolCsv = req.MapPoolCsv;
    t.MinTeams = req.MinTeams;
    t.MaxTeams = req.MaxTeams;
    t.Prize = req.Prize ?? "";
    t.IsPaidEntry = req.IsPaidEntry;
    t.EntryFee = req.EntryFee ?? "";
    await db.SaveChangesAsync();
    return Results.Ok(t);
}).RequireAuthorization();

// Inscrição com escalação de 5 + capitão (espec-times §16; espec-campeonatos §2-3)
app.MapPost("/api/tournaments/{id}/register", async (ApiDbContext db, HttpContext ctx, string id, RegisterTeamRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!; // RequireAuthorization() garante não-nulo

    var exists = await db.TournamentTeams
        .AnyAsync(x => x.TournamentId == id && x.TeamId == req.TeamId);
    if (exists) return Results.Ok(true);

    var t = await db.Tournaments.FindAsync(id);
    if (t == null) return Results.Ok(false);

    // inscrições fecham automaticamente 12h antes do início (§3)
    if (DateTime.UtcNow >= t.RegistrationClosesAt) return Results.Ok(false);
    if (t.Status != TournamentStatus.Open) return Results.Ok(false);

    var count = await db.TournamentTeams.CountAsync(x => x.TournamentId == id);
    if (count >= t.MaxTeams) return Results.Ok(false);

    var team = await db.Teams.Include(x => x.Members)
        .FirstOrDefaultAsync(x => x.Id == req.TeamId);
    if (team == null) return Results.Ok(false);

    // quem inscreve precisa ser dono ou sublíder (§16) — antes um ByUserId vazio pulava essa
    // checagem inteira; com autenticação real sempre existe um autor verificado pra checar.
    if (!await CompetitionEndpoints.IsOwnerOrSub(db, req.TeamId, authUserId))
        return Results.Ok(false);

    // escalação: 5 quando o elenco permite; senão o elenco completo (modo alpha)
    var required = Math.Min(5, team.Members.Count);
    if (required < 1) return Results.Ok(false);
    var playerIds = (req.PlayerIds != null && req.PlayerIds.Count > 0)
        ? req.PlayerIds
        : team.Members.OrderBy(m => m.TeamJoinedAt ?? DateTime.MaxValue).Take(required).Select(m => m.Id).ToList();
    var captainId = req.CaptainUserId
        ?? (playerIds.Contains(team.CaptainId) ? team.CaptainId : playerIds.FirstOrDefault());

    var error = await CompetitionEndpoints.ValidateLineupAsync(db, id, req.TeamId, playerIds, captainId, null, required);
    if (error != null) return Results.Ok(false);

    var tt = new TournamentTeam
    {
        Id = $"tt_{Guid.NewGuid():N}",
        TournamentId = id,
        TeamId = req.TeamId,
        Seed = count + 1,
        RegisteredAt = DateTime.UtcNow,
        CaptainUserId = captainId,
        CheckIn = CheckInStatus.Waiting
    };
    db.TournamentTeams.Add(tt);
    foreach (var pid in playerIds.Distinct())
        db.TournamentLineupPlayers.Add(new TournamentLineupPlayer
        {
            Id = $"lp_{Guid.NewGuid():N}",
            TournamentTeamId = tt.Id,
            UserId = pid
        });
    await CompetitionEndpoints.Audit(db, "team_registered", authUserId, null, req.TeamId, id,
        null, string.Join(",", playerIds), null);
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapGet("/api/tournaments/{id}/registered/{teamId}", async (ApiDbContext db, string id, string teamId) =>
    Results.Ok(await db.TournamentTeams.AnyAsync(x => x.TournamentId == id && x.TeamId == teamId)));

// ═════════════════════════════ MATCHES ═════════════════════════════

// histórico de partidas só é usado hoje pra "minhas partidas" (Home/Stats) — trava no token.
app.MapGet("/api/matches/recent", async (ApiDbContext db, HttpContext ctx, int take) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    return Results.Ok(await db.Matches
        .Include(m => m.Players)
        .Where(m => m.Players.Any(p => p.UserId == authUserId))
        .OrderByDescending(m => m.PlayedAt)
        .Take(take <= 0 ? 20 : take)
        .ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/matches/team/{teamId}", async (ApiDbContext db, string teamId, int take) =>
    Results.Ok(await db.Matches
        .Include(m => m.Players)
        .Where(m => m.TeamAId == teamId || m.TeamBId == teamId)
        .OrderByDescending(m => m.PlayedAt)
        .Take(take <= 0 ? 20 : take)
        .ToListAsync()));

app.MapGet("/api/matches/{id}", async (ApiDbContext db, string id) =>
{
    var m = await db.Matches
        .Include(x => x.Players).ThenInclude(p => p.User)
        .FirstOrDefaultAsync(x => x.Id == id);
    return m == null ? Results.NotFound() : Results.Ok(m);
});

// ═════════════════════════════ FRIENDS ═════════════════════════════

// lista de amigos é vista entre perfis (recurso de "amigos em comum") — só exige estar
// logado, não trava no próprio userId como incoming/outgoing (que são privados de verdade).
app.MapGet("/api/friends/{userId}", async (ApiDbContext db, string userId) =>
{
    var asRequester = db.Friendships
        .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Accepted)
        .Select(f => f.Addressee!);
    var asAddressee = db.Friendships
        .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Accepted)
        .Select(f => f.Requester!);
    return Results.Ok(await asRequester.Concat(asAddressee).OrderBy(u => u.Nickname).ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/friends/{userId}/incoming", async (ApiDbContext db, HttpContext ctx, string userId) =>
{
    if (SummitAuth.GetUserId(ctx) != userId) return Results.Forbid(); // pedidos pendentes são privados
    return Results.Ok(await db.Friendships
        .Include(f => f.Requester)
        .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
        .OrderByDescending(f => f.CreatedAt)
        .ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/friends/{userId}/outgoing", async (ApiDbContext db, HttpContext ctx, string userId) =>
{
    if (SummitAuth.GetUserId(ctx) != userId) return Results.Forbid();
    return Results.Ok(await db.Friendships
        .Include(f => f.Addressee)
        .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Pending)
        .OrderByDescending(f => f.CreatedAt)
        .ToListAsync());
}).RequireAuthorization();

app.MapGet("/api/friends/relation", async (ApiDbContext db, HttpContext ctx, string otherId) =>
{
    var viewerId = SummitAuth.GetUserId(ctx)!; // "como EU vejo esse outro usuário" — não dá pra fingir ser outro viewer
    if (viewerId == otherId) return Results.Ok("None");
    var f = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == viewerId && x.AddresseeId == otherId) ||
        (x.RequesterId == otherId && x.AddresseeId == viewerId));
    if (f == null) return Results.Ok("None");
    if (f.Status == FriendshipStatus.Blocked) return Results.Ok("Blocked");
    if (f.Status == FriendshipStatus.Accepted) return Results.Ok("Friends");
    if (f.Status == FriendshipStatus.Pending)
        return Results.Ok(f.RequesterId == viewerId ? "OutgoingPending" : "IncomingPending");
    return Results.Ok("None");
}).RequireAuthorization();

app.MapPost("/api/friends/block", async (ApiDbContext db, HttpContext ctx, FriendRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    if (authUserId == req.AddresseeId) return Results.Ok(false);
    var existing = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == authUserId && x.AddresseeId == req.AddresseeId) ||
        (x.RequesterId == req.AddresseeId && x.AddresseeId == authUserId));

    if (existing != null)
    {
        existing.Status = FriendshipStatus.Blocked;
        existing.RespondedAt = DateTime.UtcNow;
    }
    else
    {
        db.Friendships.Add(new Friendship
        {
            Id = $"fr_{Guid.NewGuid():N}",
            RequesterId = authUserId,
            AddresseeId = req.AddresseeId,
            Status = FriendshipStatus.Blocked,
            CreatedAt = DateTime.UtcNow,
            RespondedAt = DateTime.UtcNow
        });
    }
    await CompetitionEndpoints.Audit(db, "friend_blocked", authUserId, req.AddresseeId, null, null, null, null, null);
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapPost("/api/friends/request", async (ApiDbContext db, HttpContext ctx, FriendRequest req) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    if (authUserId == req.AddresseeId) return Results.Ok(false);
    var existing = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == authUserId && x.AddresseeId == req.AddresseeId) ||
        (x.RequesterId == req.AddresseeId && x.AddresseeId == authUserId));
    if (existing != null) return Results.Ok(false);

    db.Friendships.Add(new Friendship
    {
        Id = $"fr_{Guid.NewGuid():N}",
        RequesterId = authUserId,
        AddresseeId = req.AddresseeId,
        Status = FriendshipStatus.Pending,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapPost("/api/friends/{id}/accept", async (ApiDbContext db, HttpContext ctx, string id) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == id);
    if (f == null || f.AddresseeId != authUserId || f.Status != FriendshipStatus.Pending)
        return Results.Ok(false);
    f.Status = FriendshipStatus.Accepted;
    f.RespondedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapPost("/api/friends/{id}/decline", async (ApiDbContext db, HttpContext ctx, string id) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == id);
    if (f == null || f.AddresseeId != authUserId || f.Status != FriendshipStatus.Pending)
        return Results.Ok(false);
    f.Status = FriendshipStatus.Declined;
    f.RespondedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapDelete("/api/friends", async (ApiDbContext db, HttpContext ctx, string userAId, string userBId) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    if (authUserId != userAId && authUserId != userBId) return Results.Forbid(); // só uma das partes desfaz a amizade
    var f = await db.Friendships.FirstOrDefaultAsync(x =>
        (x.RequesterId == userAId && x.AddresseeId == userBId) ||
        (x.RequesterId == userBId && x.AddresseeId == userAId));
    if (f == null) return Results.Ok(false);
    db.Friendships.Remove(f);
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

// ═════════════════════════════ BADGES ═════════════════════════════

app.MapGet("/api/badges", async (ApiDbContext db) =>
    Results.Ok(await db.Badges.OrderBy(b => b.Name).ToListAsync()));

app.MapGet("/api/badges/user/{userId}", async (ApiDbContext db, string userId) =>
    Results.Ok(await (from ub in db.UserBadges
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
                      }).ToListAsync()));

app.MapGet("/api/badges/user/{userId}/all", async (ApiDbContext db, string userId) =>
{
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
    return Results.Ok(all);
});

// ═════════════════════════════ RANKING ═════════════════════════════

app.MapGet("/api/ranking/players", async (ApiDbContext db) =>
{
    var users = await db.Users
        .Include(u => u.Team)
        .OrderByDescending(u => u.Elo)
        .Take(50)
        .ToListAsync();

    var list = users.Select((u, i) => new RankingPlayer
    {
        Position = i + 1,
        UserId = u.Id,
        Nickname = u.Nickname,
        AvatarUrl = u.AvatarUrl,
        Country = u.Country,
        TeamTag = u.Team?.Tag ?? "",
        Rank = u.Rank,
        Elo = u.Elo,
        Level = u.Level,
        WinRate = u.WinRate,
        KD = u.KD,
        Matches = u.TotalMatches
    }).ToList();
    return Results.Ok(list);
});

app.MapGet("/api/ranking/teams", async (ApiDbContext db) =>
{
    var teams = await db.Teams
        .Include(t => t.Members)
        .OrderByDescending(t => t.Elo)
        .Take(50)
        .ToListAsync();

    var list = teams.Select((t, i) => new RankingTeam
    {
        Position = i + 1,
        TeamId = t.Id,
        Name = t.Name,
        Tag = t.Tag,
        Country = t.Country,
        Elo = t.Elo,
        WinRate = t.WinRate,
        TournamentsWon = t.TournamentsWon,
        Matches = t.MatchesPlayed
    }).ToList();
    return Results.Ok(list);
});

// ═════════════════════════════ NOTIFICATIONS (RF-06) ═════════════════════════════

// notificação é correio pessoal — sempre lê do token, o userId da rota é ignorado.
app.MapGet("/api/notifications/{userId}", async (ApiDbContext db, HttpContext ctx, string userId, bool? unreadOnly) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var q = db.Notifications.Where(n => n.UserId == authUserId);
    if (unreadOnly == true) q = q.Where(n => !n.IsRead);
    return Results.Ok(await q.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync());
}).RequireAuthorization();

app.MapPost("/api/notifications/{id}/read", async (ApiDbContext db, HttpContext ctx, string id) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id);
    if (n == null) return Results.NotFound();
    if (n.UserId != authUserId) return Results.Forbid(); // não dá pra marcar notificação de outro como lida
    n.IsRead = true;
    await db.SaveChangesAsync();
    return Results.Ok(true);
}).RequireAuthorization();

app.MapPost("/api/notifications/{userId}/read-all", async (ApiDbContext db, HttpContext ctx, string userId) =>
{
    var authUserId = SummitAuth.GetUserId(ctx)!;
    var unread = await db.Notifications.Where(n => n.UserId == authUserId && !n.IsRead).ToListAsync();
    foreach (var n in unread) n.IsRead = true;
    await db.SaveChangesAsync();
    return Results.Ok(unread.Count);
}).RequireAuthorization();

// Endpoints das especificações (docs/espec-*.md)
app.MapCompetitionEndpoints();

// Integração real com o MatchZy (docs/plano-aws.md) — config de partida + webhook de resultado
app.MapMatchZyEndpoints();

// 0.0.0.0 (não localhost) pra aceitar conexões de fora do container — obrigatório pro App
// Runner (Fase C); porta configurável via PORT (convenção do App Runner/outros PaaS), com
// 5180 como default pra não quebrar o fluxo local de sempre.
var port = Environment.GetEnvironmentVariable("PORT") ?? "5180";
app.Run($"http://0.0.0.0:{port}");

// ───── Request DTOs ─────
record SteamLoginRequest(string SteamId, string Nickname, string AvatarUrl);
record CreateTeamRequest(string Name, string Tag, string CaptainId);
record UpdateTeamRequest(string Name, string? Description, string? LogoUrl, string? Country, string ByUserId);
record InviteRequest(string InvitedUserId, string InvitedById);
record RegisterTeamRequest(string TeamId, string? ByUserId, List<string>? PlayerIds, string? CaptainUserId);
record FriendRequest(string RequesterId, string AddresseeId);
record FriendActionRequest(string UserId);
record RconDebugRequest(string Ip, string Command, string? Password = null);
record SimulateResultBody(string Winner);
record SetTournamentDateBody(DateTime StartDate);
record CreateTournamentRequest(string Name, string? Description, string? Region, DateTime StartDate,
    TournamentFormat FormatType, SeriesFormat Series, SeriesFormat FinalSeries, string MapPoolCsv,
    int MinTeams, int MaxTeams, string? Prize, bool IsPaidEntry, string? EntryFee,
    string OrganizerUserId, string? OrganizerName);
record UpdateTournamentRequest(string Name, string? Description, string? Region, DateTime StartDate,
    TournamentFormat FormatType, SeriesFormat Series, SeriesFormat FinalSeries, string MapPoolCsv,
    int MinTeams, int MaxTeams, string? Prize, bool IsPaidEntry, string? EntryFee, string ByUserId);
