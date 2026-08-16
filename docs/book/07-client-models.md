[← Sumário](00-indice.md)

# Capítulo 7 — Modelos do Client

O [Capítulo 4](04-banco-dados.md) já cobriu os `Models/*.cs` do ponto de vista do banco de dados
(o que é persistido, como). Este capítulo olha para os mesmos arquivos do ponto de vista de quem
consome no client — e cataloga também uma segunda categoria de tipo que **não é compartilhada
com a API**: pequenos DTOs de apresentação que vivem dentro dos próprios arquivos de ViewModel.

## 7.1 Duas categorias de "modelo" no client

1. **Modelos compartilhados** (`Models/*.cs`): vêm de/vão para a API via JSON, têm o mesmo
   formato nos dois lados (ver [§2.1.1](02-arquitetura.md#211-como-eles-compartilham-código)).
   Exemplos: `User`, `Team`, `Tournament`, `Match`, `VetoSession`.
2. **DTOs de apresentação locais**: classes simples definidas dentro do próprio arquivo de
   ViewModel que as usa, existem só em memória no client, nunca trafegam como tal pela rede —
   são *derivadas* de um modelo compartilhado, moldadas especificamente para o que aquela tela
   precisa mostrar. Exemplos: `MatchListItem`, `ScoreboardRow`, `VetoMapItem`,
   `BracketColumnViewModel`.

Entender essa distinção evita um erro comum ao explorar o código: procurar `MatchListItem` em
`Models/` (não está lá — está em `ViewModels/HomeViewModel.cs` e `ViewModels/MatchesViewModel.cs`,
cada arquivo com sua própria cópia da classe, porque as duas telas a usam de forma ligeiramente
diferente e nenhuma delas depende da outra).

## 7.2 Por que existem DTOs de apresentação separados dos modelos compartilhados

Pegue `Match`/`MatchPlayer` (compartilhado) versus `MatchListItem` (local, em `HomeViewModel.cs`
e `MatchesViewModel.cs`):

```csharp
// Models/Match.cs — a entidade completa, como vem da API
public class Match
{
    public string TeamAId { get; set; } = string.Empty;
    public List<MatchPlayer> Players { get; set; } = new();
    // ... 15+ campos
}
```

```csharp
// ViewModels/MatchesViewModel.cs — só o que a lista de partidas precisa mostrar
public class MatchListItem
{
    public string Id { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public bool Won { get; set; }
    public int Kills { get; set; }
    public string KDA => $"{Kills} / {Deaths} / {Assists}";
    public string WonLabel => Won ? "VITÓRIA" : "DERROTA";
}
```

`MatchListItem` já vem com `Won` calculado (comparando o `TeamSide` do jogador atual com quem
venceu) e rótulos textuais prontos (`WonLabel`, `DateLabel`) — o ViewModel faz essa transformação
uma vez, ao carregar (`raw.Select(m => new MatchListItem { ... })`), para que o XAML da lista só
precise fazer *binding* direto a texto pronto, sem nenhuma lógica ou conversor no XAML. Essa é a
razão de existir: **separar "o formato que a API devolve" de "o formato que esta tela específica
quer desenhar"**, para que mudanças puramente visuais numa tela não tenham nenhum efeito sobre o
modelo de rede.

## 7.3 Catálogo dos DTOs de apresentação locais

| Classe | Onde vive | Constrói a partir de | Usado por |
|---|---|---|---|
| `MatchListItem` | `HomeViewModel.cs` e `MatchesViewModel.cs` (duas cópias independentes) | `Match` + `MatchPlayer` do usuário atual | Lista de partidas recentes |
| `ScoreboardRow` | `MatchDetailsViewModel.cs` | `MatchPlayer` | Placar detalhado pós-partida (dois lados A/B) |
| `VetoMapItem` | `MatchRoomViewModel.cs` | `VetoSession`/`VetoStep`/`VetoState` | Grade de mapas clicável durante o veto |
| `BracketSlotViewModel` / `BracketColumnViewModel` | `ViewModels/BracketLayout.cs` | `BracketRound`/`BracketMatch` | Renderização da chave (ver [Capítulo 18](18-feature-bracket.md)) |
| `LineupMemberItem` | `LineupViewModel.cs` | `User` (membro do time) | Seleção da escalação (ver [Capítulo 17](17-feature-escalacao.md)) |
| `SidebarItem` | `MainShellViewModel.cs` | estático (ícone/label/comando) | Itens do menu lateral |
| `FilterItem` | `TournamentsViewModel.cs` | estático (nome do filtro) | Chips de filtro de campeonatos |

Todos os que precisam de interatividade própria (seleção, clique) herdam `BaseViewModel`
(`LineupMemberItem`, `SidebarItem`, `FilterItem`, `BracketSlotViewModel` como exceção — este é
`class` simples porque seus dados são só de leitura para desenho, nunca mudam depois de
construído). Os que são só exibição de dados já prontos (`MatchListItem`, `ScoreboardRow`,
`VetoMapItem`) são `class` comuns — não precisam notificar mudança porque são recriados do zero a
cada carregamento, nunca mutados individualmente depois de existir.

## 7.4 Como um modelo compartilhado carrega lógica de apresentação sem virar DTO local

Nem toda necessidade de apresentação gera um DTO local — muita coisa fica direto no modelo
compartilhado como propriedade computada, quando faz sentido em qualquer tela que use aquele
modelo (não é específico de uma tela). `Models/Tournament.cs` é o exemplo mais denso disso:

```csharp
public string StatusLabel => Status switch
{
    TournamentStatus.Open       => "INSCRIÇÕES ABERTAS",
    TournamentStatus.InProgress => "EM ANDAMENTO",
    TournamentStatus.Finished   => "ENCERRADO",
    TournamentStatus.Upcoming   => "EM BREVE",
    _                           => ""
};

public string CountdownLabel { get { /* "EM 3 DIAS" / "EM 2H 15M" / "AO VIVO AGORA" / ... */ } }
public string TeamsCountText => $"{RegisteredTeams}/{MaxTeams}";
public double SlotsFillPercent => MaxTeams > 0 ? (double)RegisteredTeams / MaxTeams : 0;
```

**Regra prática para decidir onde colocar uma nova propriedade de exibição**: se o cálculo só usa
campos que já existem no modelo compartilhado e faria sentido em *qualquer* tela que mostre
aquele objeto (ex. "está com inscrições abertas?" é útil tanto na lista de campeonatos quanto na
tela de detalhes), coloque como propriedade computada direto no `Models/`. Se o cálculo depende de
*contexto específico de uma tela* (ex. "esse item de partida foi vitória *para o usuário logado
agora*" — depende de quem está olhando, não é uma verdade universal do objeto `Match`), isso vai
num DTO local daquela tela, como visto em `MatchListItem.Won`.

Lembre que toda propriedade computada adicionada a um `Models/*.cs` que é uma entidade EF Core
precisa do `.Ignore(...)` correspondente em `ApiDbContext.OnModelCreating`
(ver [§4.3.2](04-banco-dados.md#432-propriedades-computadas-são-explicitamente-ignoradas)) — esse
passo é fácil de esquecer justamente porque, do lado do client, tudo parece funcionar sem ele (o
client não sabe nada sobre EF Core); o erro só aparece do lado da API na próxima vez que o schema
for recriado do zero.

## 7.5 `IsRegistered`: um campo "calculado à mão" pelo Service, não pela API

Vale destacar um padrão específico de `Tournament.IsRegistered` porque foge da regra geral acima
— ele **não** é uma propriedade computada a partir de outros campos do próprio objeto (não tem
como saber, olhando só para um `Tournament`, se o time do usuário atual está inscrito nele). Em
vez disso, é um campo mutável comum (`public bool IsRegistered { get; set; }`), com o comentário
"set by service when loading for current user" — e de fato é o `TournamentService` do client quem
o preenche depois de buscar a lista, com uma chamada extra por campeonato:

```csharp
// Services/TournamentService.cs
private async Task MarkRegisteredAsync(List<Tournament> list)
{
    var teamId = App.UserService.CurrentUser?.TeamId;
    if (string.IsNullOrEmpty(teamId)) return;
    foreach (var t in list)
        t.IsRegistered = await _repo.IsTeamRegisteredAsync(t.Id, teamId);
}
```

Isso é um bom exemplo de que nem toda informação relevante para a tela é um campo "puro" do
domínio — `IsRegistered` é fundamentalmente uma pergunta relativa ("registrado *em relação a
qual time*?"), então ela é resolvida na camada de Service do client, sob demanda, e anexada ao
objeto antes de chegar ao ViewModel. O trade-off explícito aqui é uma chamada HTTP extra por
campeonato listado (`N+1`) em troca de manter a API simples (um único endpoint de listagem, sem
precisar saber "para qual usuário" a listagem é). Para o volume de dados deste produto (dezenas
de campeonatos, não milhares), esse custo é aceitável; seria o primeiro ponto a otimizar se a
lista de campeonatos crescesse muito.
