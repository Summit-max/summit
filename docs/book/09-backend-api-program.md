[← Sumário](00-indice.md)

# Capítulo 9 — Program.cs e o Bootstrap da API

## 9.1 A sequência de inicialização

`Summit.Api/Program.cs` segue a estrutura padrão de um app ASP.NET Core moderno (top-level
statements, sem `Main` explícito), na ordem:

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Escolhe o provedor de banco (MySQL via env, senão SQLite local)
var mysql = Environment.GetEnvironmentVariable("SUMMIT_DB") ?? builder.Configuration.GetConnectionString("MySql");
builder.Services.AddDbContext<ApiDbContext>(o => { /* ... */ });

// 2. Configura serialização JSON (mesma opção do client, ver Capítulo 6)
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// 3. Registra os três BackgroundServices + o MatchServerService singleton
builder.Services.AddHostedService<LifecycleWorker>();
builder.Services.AddSingleton<MatchServerService>();
builder.Services.AddHostedService<ServerProvisionPoller>();
builder.Services.AddHostedService<PoolManagerService>();

var app = builder.Build();

// 4. Cria o schema (se não existir) e semeia dados de demonstração (se o banco estiver vazio)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();
    await SeedData.EnsureSeededAsync(db);
}

// 5. Centenas de linhas de app.MapGet/MapPost/MapPut/MapDelete ...

app.MapCompetitionEndpoints();   // 6. registra o segundo arquivo de rotas

app.Run("http://localhost:5180");   // 7. porta fixa, sempre a mesma
```

A porta é **fixa e hardcoded** (`5180`) — não vem de configuração nem variável de ambiente. Isso
é consistente com o `ApiClient` do client, cujo padrão (`http://localhost:5180`) é exatamente
esse valor; se algum dia a porta precisar ser configurável, os dois lados (aqui e
`Services/ApiClient.cs`) precisam mudar juntos, ou usar `SUMMIT_API_URL` do lado do client para
apontar para uma porta diferente sem tocar aqui.

## 9.2 `MatchServerService` como `Singleton`, os workers como `HostedService`

```csharp
builder.Services.AddSingleton<MatchServerService>();
builder.Services.AddHostedService<ServerProvisionPoller>();
builder.Services.AddHostedService<PoolManagerService>();
```

`MatchServerService` é registrado como `Singleton` porque ele não guarda nenhum estado próprio
entre chamadas (não tem campo mutável de instância) — é seguro compartilhar a mesma instância
entre requisições HTTP concorrentes e os BackgroundServices. Os workers (`LifecycleWorker`,
`ServerProvisionPoller`, `PoolManagerService`) são registrados como `AddHostedService`, o
mecanismo padrão do ASP.NET Core para "processos de fundo que rodam durante toda a vida do app" —
o framework os inicia automaticamente no `app.Build()`/início do host e os para de forma
ordenada no shutdown.

Um detalhe de DI importante: como o `ApiDbContext` é *scoped* (uma instância por requisição HTTP,
o padrão do EF Core), e os workers vivem fora do ciclo de uma requisição, cada um precisa **criar
seu próprio escopo manualmente** a cada iteração do loop, em vez de receber o `ApiDbContext`
injetado direto no construtor:

```csharp
// padrão repetido nos três workers
using var scope = _scopes.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
```

Isso é resolvido injetando `IServiceScopeFactory` (não `ApiDbContext`) no construtor do worker —
ver [Capítulo 11](11-backend-services-workers.md) para o padrão completo.

## 9.3 Criação do schema e seed — o que acontece na primeira subida

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();
    await SeedData.EnsureSeededAsync(db);
}
```

Esse bloco roda **toda vez que a API sobe**, não só na primeira vez — mas ambas as chamadas são
idempotentes por design: `EnsureCreated()` não faz nada se as tabelas já existem (ver
[§4.2](04-banco-dados.md#42-a-decisão-consciente-de-não-usar-migrations)), e
`SeedData.EnsureSeededAsync` começa checando se já existe pelo menos um usuário:

```csharp
public static async Task EnsureSeededAsync(ApiDbContext db)
{
    bool hasUsers = await db.Users.AnyAsync();
    if (hasUsers) return;
    // ... só semeia se o banco estiver completamente vazio
}
```

Isso significa: subir a API contra um banco que já tem dados reais de uso **nunca reintroduz os
dados de demonstração** — o seed só roda mesmo na primeiríssima vez que aquele banco é usado.
Para forçar o seed de novo (por exemplo, depois de limpar o banco de dev para testar do zero), é
preciso apagar todas as linhas de `users` (ou o banco inteiro) antes de subir a API.

## 9.4 Os endpoints de diagnóstico (`/api/debug/*`)

Antes mesmo dos endpoints de domínio (`/api/users`, `/api/teams`, etc.), `Program.cs` define uma
dezena de rotas `/api/debug/*` — esse é o padrão já descrito em
[§3.10](03-padroes-projeto.md#310-padrão-de-endpoint-de-debug-dev-only-deliberado). Referência
rápida do que cada uma faz (todas fora do escopo de configuração AWS deste livro, mas relevantes
para entender a superfície da API):

| Endpoint | Método | Propósito |
|---|---|---|
| `/api/debug/ami-status` | GET | confere se a AMI configurada terminou de "empacotar" na AWS |
| `/api/debug/instances` | GET | lista instâncias EC2 tagueadas pelo Summit (`summit:matchId`/`summit:pool`/`summit:manual`) |
| `/api/debug/instances/{id}/terminate` | POST | termina uma instância manualmente |
| `/api/debug/instances/{id}/stop` | POST | para (sem terminar) uma instância |
| `/api/debug/rcon` | POST | manda um comando RCON ad-hoc a um IP (usa senha do pool se conhecida, senão a do corpo) |
| `/api/debug/security-group` | GET | mostra as regras inbound/outbound do Security Group configurado |
| `/api/debug/generate-bracket/{tournamentId}` | POST | gera a chave imediatamente, sem esperar os horários automáticos (ver Capítulo 18) |
| `/api/debug/launch-bare-instance` | POST | sobe uma EC2 pura da AMI, sem User Data — para configuração manual via SSH |
| `/api/debug/volumes` | GET | lista volumes EBS, sinalizando órfãos (`Orphan = Attachments.Count == 0`) |
| `/api/debug/snapshots` | GET | lista snapshots EBS próprios |
| `/api/debug/pool` | GET | estado atual do pool de servidores CS2 (ver Capítulo 20) |

Nenhum desses endpoints tem autenticação — são ferramentas internas de operação, não superfície
de produto (ver a ressalva de segurança em
[§3.10](03-padroes-projeto.md#310-padrão-de-endpoint-de-debug-dev-only-deliberado)).

## 9.5 Onde cada domínio de endpoint "de produto" mora

`Program.cs` organiza os endpoints de domínio em blocos comentados com separadores visuais
(`// ═══ USERS ═══`, etc.), todos no mesmo arquivo, na ordem: **Users → Teams → Tournaments →
Matches → Friends → Badges → Ranking**. O segundo arquivo, `CompetitionEndpoints.cs`, cobre um
conjunto diferente e mais elaborado de regras (solicitações de entrada, cargos, check-in,
escalação, veto, auditoria) — a distinção entre "vai em `Program.cs`" versus "vai em
`CompetitionEndpoints.cs`" não é por domínio de dado, mas por **origem**: tudo que veio
diretamente das especificações funcionais (`docs/espec-times.md`, `docs/espec-campeonatos.md`)
foi implementado em `CompetitionEndpoints.cs`, cujo comentário de topo deixa isso explícito:

```csharp
/// Endpoints das especificações funcionais (docs/espec-campeonatos.md e docs/espec-times.md).
/// Regra central de segurança (§43): toda permissão é validada aqui no backend.
public static class CompetitionEndpoints
{
    public static void MapCompetitionEndpoints(this WebApplication app) { /* ... */ }
}
```

O [Capítulo 10](10-backend-endpoints.md) cataloga cada rota dos dois arquivos.
