[← Sumário](00-indice.md)

# Capítulo 2 — Arquitetura Geral

## 2.1 Os dois projetos

O repositório tem dois projetos .NET lado a lado:

| Projeto | Arquivo | SDK | Tipo | Roda onde |
|---|---|---|---|---|
| Client | `Summit.csproj` | `Microsoft.NET.Sdk` + `UseWPF` | Executável Windows (`WinExe`) | Máquina do jogador |
| API | `Summit.Api/Summit.Api.csproj` | `Microsoft.NET.Sdk.Web` | Web API (Kestrel) | Servidor (ou `localhost` em dev) |

Ambos miram `.NET 8` (o client em `net8.0-windows` porque usa WPF, que é Windows-only; a API em
`net8.0` puro, porque não tem nenhuma dependência de UI).

### 2.1.1 Como eles compartilham código

Olhando o `Summit.Api.csproj`:

```xml
<!-- Modelos compartilhados com o client WPF -->
<ItemGroup>
  <Compile Include="..\Models\*.cs" LinkBase="Models" />
</ItemGroup>
```

Isso não importa um pacote — ele **inclui os mesmos arquivos-fonte** da pasta `Models/` do client
diretamente na compilação da API, via link (`LinkBase`, sem copiar fisicamente o arquivo). Na
prática, isso quer dizer que existe *um* arquivo `Models/Tournament.cs`, e tanto `Summit.exe`
quanto `Summit.Api.exe` o compilam para dentro de si. Não existe risco de o client ter um
`Tournament` com um campo a mais ou a menos do que a API espera — é fisicamente o mesmo tipo.

A implicação prática mais importante disso: **quando você adiciona um campo em `Models/`, os
dois lados enxergam automaticamente** (depois de recompilar os dois). Não existe um passo de
"gerar DTO" ou "atualizar contrato" — o contrato *é* o código.

## 2.2 Como os dois se comunicam

Só por HTTP/JSON, simples assim. Não há gRPC, SignalR ou WebSocket em lugar nenhum do sistema —
mesmo telas que parecem "tempo real" (como a sala de partida durante o veto) são implementadas
com **polling**: o client pergunta de novo a cada poucos segundos.

- O client fala com a API inteiramente através de `Services/ApiClient.cs` — uma classe estática
  com um único `HttpClient` compartilhado, que aponta para `SUMMIT_API_URL` (ou
  `http://localhost:5180` por padrão). Ver [Capítulo 6](06-client-navegacao-api.md) para os
  detalhes desse cliente.
- A API expõe tudo via **Minimal API** do ASP.NET Core — não há Controllers, não há MVC, é tudo
  `app.MapGet(...)`/`app.MapPost(...)` direto no `Program.cs` (e em
  `Summit.Api/CompetitionEndpoints.cs`, que é um segundo arquivo de rotas organizado por método
  de extensão `MapCompetitionEndpoints(this WebApplication app)`).

Um exemplo real do padrão (client → API) para pegar um campeonato por id:

```csharp
// Data/TournamentRepository.cs
public Task<Tournament?> GetByIdAsync(string id)
    => ApiClient.GetAsync<Tournament>($"/api/tournaments/{id}");
```

```csharp
// Summit.Api/Program.cs
app.MapGet("/api/tournaments/{id}", async (ApiDbContext db, string id) =>
{
    var t = await db.Tournaments
        .Include(x => x.TournamentTeams).ThenInclude(tt => tt.Team).ThenInclude(tm => tm!.Members)
        .Include(x => x.Bracket).ThenInclude(r => r.Matches)
        .FirstOrDefaultAsync(x => x.Id == id);
    return t == null ? Results.NotFound() : Results.Ok(t);
});
```

O client nunca monta SQL, nunca sabe que existe MySQL por trás — ele só sabe que existe um
caminho HTTP que devolve um `Tournament` em JSON.

## 2.3 Persistência

A API usa **Entity Framework Core** com dois provedores possíveis, escolhidos em runtime pelo
`Program.cs`:

```csharp
var mysql = Environment.GetEnvironmentVariable("SUMMIT_DB")
         ?? builder.Configuration.GetConnectionString("MySql");

builder.Services.AddDbContext<ApiDbContext>(o =>
{
    if (!string.IsNullOrWhiteSpace(mysql))
        o.UseMySql(mysql, ServerVersion.AutoDetect(mysql));
    else
        o.UseSqlite($"Data Source={Path.Combine(builder.Environment.ContentRootPath, "summit-api.db")}");
});
```

Se a variável de ambiente `SUMMIT_DB` (ou a connection string `MySql` do `appsettings.json`)
estiver definida, usa **MySQL** (via o provedor Pomelo) — esse é o modo "de verdade", usado no
dia a dia deste projeto. Se não estiver definida, cai automaticamente para um arquivo **SQLite**
local (`summit-api.db`) — um modo de conveniência para rodar a API sem precisar instalar MySQL
(por exemplo, em uma máquina nova ou em CI). Os dois caminhos usam exatamente o mesmo
`ApiDbContext` e o mesmo conjunto de entidades — só troca o motor por baixo.

Importante: **não existe sistema de migrations** neste projeto. O schema é criado com
`db.Database.EnsureCreated()` no startup, uma única vez (se as tabelas já existem, não faz nada).
Isso significa que qualquer mudança de schema depois que o banco já existe precisa de um
`ALTER TABLE` manual — ver o [Capítulo 4](04-banco-dados.md#42-a-decisão-consciente-de-não-usar-migrations)
para a explicação completa dessa decisão e como aplicar mudanças na prática.

## 2.4 Os três processos de fundo da API

Além de responder requisições HTTP, o processo da API roda três `BackgroundService` em paralelo,
registrados no `Program.cs`:

```csharp
builder.Services.AddHostedService<LifecycleWorker>();
builder.Services.AddSingleton<MatchServerService>();
builder.Services.AddHostedService<ServerProvisionPoller>();
builder.Services.AddHostedService<PoolManagerService>();
```

| Worker | Tick | Responsabilidade |
|---|---|---|
| `LifecycleWorker` | 20s | Fecha check-in, remove ausentes, gera a chave, inicia o campeonato na hora certa, abre vetos, roda bots de veto para contas demo |
| `ServerProvisionPoller` | 10s | Acompanha instâncias EC2 criadas sob demanda ("cold boot"), grava o IP assim que fica pronta |
| `PoolManagerService` | 30s | Mantém N servidores CS2 sempre ligados e prontos, confirma via RCON que estão de fato utilizáveis, libera automaticamente os que ficaram vazios |

Cada um roda em loop infinito (`while (!ct.IsCancellationRequested)`) com um `try/catch` que
nunca deixa uma exceção matar o worker — só loga e tenta de novo no próximo tick. Isso é uma
decisão deliberada: é preferível que um tick falhe silenciosamente e tente de novo em 10-30s do
que a falha de uma verificação (ex.: uma chamada à AWS que deu timeout) derrubar o processo
inteiro da API.

Esses workers são detalhados no [Capítulo 11](11-backend-services-workers.md) e sua lógica de
produto (por que existem, o que resolvem) está espalhada pelos capítulos de feature
correspondentes (Chave no [Capítulo 18](18-feature-bracket.md), Pool de servidores no
[Capítulo 20](20-feature-pool-servidores.md)).

## 2.5 Estrutura de pastas

```
Wallbang/                          (nome da pasta no disco — o produto se chama Summit)
├── Summit.csproj                  ← projeto do client WPF
├── App.xaml / App.xaml.cs         ← bootstrap do client, monta os services estáticos
├── Commands/RelayCommand.cs       ← implementação de ICommand para binding de botões
├── Helpers/                       ← IValueConverter's usados no XAML
├── Components/                    ← UserControls reutilizáveis (ex. LevelBadge)
├── Models/                        ← COMPARTILHADO com a API (ver 2.1.1)
├── Data/                          ← Repositórios do client (uma classe por área, fala com ApiClient)
├── Services/                      ← Services do client (regra de UI/orquestração) + auth Steam
│   └── Interfaces/                ← interfaces dos services principais
├── ViewModels/                    ← um ViewModel por tela (ou por sub-fluxo de tela)
├── Views/                         ← XAML + code-behind mínimo (1:1 com ViewModels)
├── Resources/                     ← design system: cores, tipografia, estilos de botão/input/card
│
├── Summit.Api/                    ← projeto da API (Sdk.Web)
│   ├── Summit.Api.csproj
│   ├── Program.cs                 ← bootstrap + a maioria dos endpoints REST
│   ├── ApiDbContext.cs             ← mapeamento EF Core de todas as entidades
│   ├── CompetitionEndpoints.cs    ← endpoints das especificações de time/campeonato + helpers de regra
│   ├── LifecycleWorker.cs         ← motor do ciclo de vida do campeonato
│   ├── MatchServerService.cs      ← provisionamento AWS (cold-boot e pool) + RCON de alto nível
│   ├── PoolManagerService.cs      ← mantém o pool de servidores quentes
│   ├── ServerProvisionPoller.cs   ← acompanha cold-boot
│   ├── RconClient.cs              ← cliente do protocolo Source RCON, escrito na mão
│   └── SeedData.cs                ← dados de demonstração (usuários, times, campeonatos, partidas)
│
├── database/
│   ├── schema.sql                 ← dump do schema MySQL (referência, não é aplicado automaticamente)
│   └── start-mysql.ps1            ← sobe o MySQL local de dev
│
└── docs/
    ├── espec-times.md             ← especificação funcional de times/perfis/amizades
    ├── espec-campeonatos.md       ← especificação funcional do fluxo de campeonato
    ├── plano-aws.md               ← plano + histórico de infraestrutura AWS (fora do escopo deste livro)
    ├── pendencias.md              ← lista viva do que falta/precisa melhorar
    └── book/                      ← você está aqui
```

Note a simetria: para quase toda pasta do client (`Data/`, `Services/`, `ViewModels/`) existe uma
contraparte conceitual do lado da API, mas a API não replica a mesma divisão em pastas — ela é
pequena o suficiente para caber em poucos arquivos por domínio (`Program.cs` para tudo que é
CRUD simples, `CompetitionEndpoints.cs` para a lógica de regras mais elaborada).

## 2.6 Ambientes e configuração

Tudo que muda entre "minha máquina" e "produção" é lido de variáveis de ambiente — não existe
`appsettings.Production.json` customizado neste projeto. As principais:

| Variável | Lido por | Efeito |
|---|---|---|
| `SUMMIT_API_URL` | client (`ApiClient`) | endereço da API; padrão `http://localhost:5180` |
| `SUMMIT_DB` | API (`Program.cs`) | connection string MySQL; ausente = usa SQLite local |
| `SUMMIT_STEAM_API_KEY` | client (`SteamConfig`) | chave da Steam Web API para buscar nick/avatar reais |
| `AWS_ACCESS_KEY_ID` / `AWS_REGION` / `SUMMIT_AMI_ID` / ... | API (`MatchServerService`) | credenciais e parâmetros AWS (fora do escopo deste livro — ver `docs/plano-aws.md`) |
| `SUMMIT_POOL_SIZE` | API (`PoolManagerService`) | quantos servidores CS2 manter sempre quentes (padrão `1`) |

Essa é uma escolha intencional de simplicidade: qualquer pessoa consegue rodar o sistema inteiro
localmente sem tocar em nenhum arquivo de configuração — só definindo (ou não) essas variáveis
antes de iniciar os dois executáveis.
