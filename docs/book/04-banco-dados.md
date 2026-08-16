[← Sumário](00-indice.md)

# Capítulo 4 — Modelo de Dados Completo

## 4.1 Visão geral do schema

O banco (`database/schema.sql`, MySQL 8.x, charset `utf8mb4`) tem 17 tabelas no dump de referência
(mais `poolservers`, criada depois via `ALTER`/`CREATE TABLE` manual e ainda não refletida nesse
dump — ver a ressalva em [§4.7](#47-como-recriar-o-banco-do-zero-fluxo-de-dev) — o que dá 18 no
total, batendo com os 18 `DbSet`s do `ApiDbContext`, ver [§23.2](23-referencia-classes-api.md#232-apidbcontext--dbsets)).
Todas usam chave
primária `varchar(255)` com um prefixo legível por tipo (`usr_`, `team_`, `trn_`, `bm_`, `rnd_`,
`m_`, `mp_`, `fr_`, `inv_`, `jrq_`, `lp_`, `veto_`, `vst_`, `aud_`, `pool_`) gerado em código como
`$"usr_{Guid.NewGuid():N}"` — nunca um `int IDENTITY`. Isso é uma escolha simples que evita
colisão entre ambientes (dá para gerar um id sem tocar o banco) e torna qualquer id imediatamente
legível em um log (`usr_a1b2c3...` já diz "isso é um usuário").

Diagrama de relacionamento (setas = chave estrangeira apontando para quem "possui" a linha):

```
users ──┬─< teaminvitations >──┬── users            friendships (users ──< requester/addressee >── users)
        │                       │
        ├── (TeamId, opcional) team
        │
teams ──┴─< teamjoinrequests >── users
      └─< tournamentteams >── tournaments
                │
                ├─< tournamentlineupplayers >── users
                │
tournaments ──< bracketrounds ──< bracketmatches
                                        │
                                        └── (MatchId opcional) matches ──< matchplayers >── users

bracketmatches ──< vetosessions ──< vetosteps

badges ──< userbadges >── users

auditlogs        (sem FK — referências soltas por id, texto livre em Action/OldValue/NewValue)
poolservers      (sem FK — não referencia nenhuma outra tabela; CurrentMatchId é um id solto)
```

## 4.2 A decisão consciente de não usar migrations

O `Program.cs` cria o schema assim:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    db.Database.EnsureCreated();
    await SeedData.EnsureSeededAsync(db);
}
```

`EnsureCreated()` olha se o banco de dados de destino já tem tabelas; se não tiver nenhuma, cria
o schema inteiro a partir do que está mapeado em `ApiDbContext.OnModelCreating`. **Se o banco já
existe** (mesmo que desatualizado em relação ao código atual), `EnsureCreated()` não faz nada —
ele não sabe comparar "schema atual" com "schema desejado" e aplicar a diferença; essa
capacidade de diff é exatamente o que o EF Core Migrations resolveria, e este projeto optou por
não usar Migrations.

Por quê? Migrations exigem manter um histórico de arquivos de migração versionados e rodar
`dotnet ef database update` a cada mudança — overhead real para um projeto em fase de
desenvolvimento rápido, onde o schema muda com frequência e o banco de dev é recriado do zero
sem custo. A troca é simples: **em desenvolvimento**, se o schema mudou, dropar e recriar o
banco de teste é mais rápido que escrever uma migration. **Contra um banco que já tem dados que
você quer preservar** (o caso mais comum neste projeto, onde o MySQL de dev acumula dados de
teste ao longo de sessões), a mudança de schema precisa ser aplicada manualmente com
`ALTER TABLE`, do jeito que já foi feito várias vezes ao longo deste projeto — por exemplo,
quando o campo `Side` foi adicionado a `BracketRound`:

```sql
ALTER TABLE bracketrounds ADD COLUMN Side INT NOT NULL DEFAULT 0;
```

**Regra prática para quem for mexer no schema**: sempre que você adicionar um campo a uma classe
em `Models/*.cs` que precisa ser persistido, você precisa fazer *duas* coisas, não uma:
1. Mapear o campo (ou confirmar que o EF Core vai inferir corretamente) em
   `ApiDbContext.OnModelCreating`.
2. Rodar o `ALTER TABLE` correspondente contra qualquer banco MySQL que já exista e que você
   queira continuar usando (local de dev, ou qualquer outro).

Esquecer o passo 2 resulta em uma exceção do EF Core em runtime (`Unknown column '...' in
'field list'`) na primeira query que tocar esse campo — não em um erro de compilação, porque o
C# compila normalmente.

## 4.3 `ApiDbContext` — como o mapeamento funciona

`Summit.Api/ApiDbContext.cs` é o único lugar que sabe a forma exata do banco. Alguns padrões que
se repetem para cada entidade:

### 4.3.1 Enums viram `int`

```csharp
user.Property(u => u.TeamRole).HasConversion<int>();
tour.Property(t => t.Status).HasConversion<int>();
tt.Property(x => x.CheckIn).HasConversion<int>();
round.Property(r => r.Side).HasConversion<int>();
```

Por padrão, o EF Core já mapeia `enum` para `int` automaticamente — essas linhas são explícitas
principalmente por clareza/documentação e para blindar contra qualquer mudança futura de
comportamento padrão. Sem essa conversão (ou se alguém trocasse para `HasConversion<string>()`),
o valor gravado mudaria de tipo e quebraria dados já existentes — outro motivo para nunca
reordenar os membros de um `enum` que já tem dados gravados (ver §4.6).

### 4.3.2 Propriedades computadas são explicitamente ignoradas

```csharp
tour.Ignore(t => t.IsRegistered);
tour.Ignore(t => t.MapPool);
tour.Ignore(t => t.RegistrationClosesAt);
// ... (uma dezena de outras)
```

Toda propriedade *getter-only* dos Models (ver [§3.9](03-padroes-projeto.md#39-propriedades-computadas-nos-models-compartilhados))
precisa aparecer aqui, ou o EF Core tenta mapeá-la como coluna e falha na criação do schema
(porque não tem setter). Isso é um ponto de atenção real ao adicionar uma propriedade computada
nova em qualquer Model: **se você esquecer de adicionar o `.Ignore(...)` correspondente, o
`EnsureCreated()` da próxima vez que o banco for recriado do zero vai falhar** (em um banco já
existente pode nem dar erro imediato, dependendo do tipo, o que torna esse esquecimento ainda
mais traiçoeiro de detectar).

### 4.3.3 Relacionamentos: `HasOne`/`WithMany` + `OnDelete`

```csharp
user.HasOne(u => u.Team)
    .WithMany(t => t.Members)
    .HasForeignKey(u => u.TeamId)
    .OnDelete(DeleteBehavior.SetNull);
```

`DeleteBehavior.SetNull` aqui significa: se um `Team` for excluído, os `User.TeamId` dos membros
viram `null` automaticamente (em vez de o banco recusar o delete, ou de apagar os usuários em
cascata — as duas alternativas seriam erradas: excluir um time não deveria excluir contas de
jogador). Compare com:

```csharp
inv.HasOne(i => i.Team)
    .WithMany(t => t.Invitations)
    .HasForeignKey(i => i.TeamId)
    .OnDelete(DeleteBehavior.Cascade);
```

Aqui, excluir um `Team` **apaga em cascata** seus convites pendentes — faz sentido, um convite
para um time que não existe mais não tem porque existir. Cada relacionamento do
`OnModelCreating` foi pensado individualmente sobre qual `DeleteBehavior` é semanticamente
correto; não existe uma regra única aplicada cegamente. Uma tabela-resumo do que está configurado
hoje:

| Relacionamento | Comportamento ao apagar o "pai" |
|---|---|
| `Team` → `User.TeamId` | `SetNull` (usuário sobrevive, sem time) |
| `Team` → `TeamInvitation` | `Cascade` |
| `Team` → `TeamJoinRequest` | `Cascade` |
| `Tournament` → `TournamentTeam` | `Cascade` |
| `Tournament` → `BracketRound` | `Cascade` |
| `BracketRound` → `BracketMatch` | `Cascade` |
| `Match` → `MatchPlayer` | `Cascade` |
| `User` → `Friendship` (como requester) | `Cascade` |
| `User` → `Friendship` (como addressee) | `NoAction` |
| `TournamentTeam` → `TournamentLineupPlayer` | `Cascade` |
| `VetoSession` → `VetoStep` | `Cascade` |
| `Badge`/`User` → `UserBadge` | `Cascade` nos dois lados |

O caso de `Friendship` merece nota: os dois lados do relacionamento apontam para `User`
(`RequesterId` e `AddresseeId`), e o EF Core/MySQL não permite dois `ON DELETE CASCADE` na mesma
tabela apontando ambos para a mesma tabela-pai por caminhos diferentes sem risco de ciclo — por
isso um lado é `Cascade` e o outro `NoAction`. Isso é uma limitação técnica do banco, não uma
escolha de negócio; na prática ambos os lados deveriam se comportar igual (uma amizade some se
qualquer um dos dois usuários for excluído), mas hoje só o lado `RequesterId` limpa
automaticamente.

## 4.4 Tabela por tabela

### `users`
A entidade central. Guarda tanto identidade (SteamId, Nickname) quanto estatísticas agregadas
(`KD`, `WinRate`, `TotalKills`...) diretamente na linha do usuário — **não há uma tabela separada
de "stats por partida" agregada em views; as estatísticas mostradas em telas como Perfil ou
Ranking vêm direto dessas colunas**, que hoje são preenchidas apenas pelo `SeedData.cs` (dados de
demonstração) porque não existe (ainda) nenhum processo que recalcule essas colunas a partir de
partidas reais — ver [Capítulo 21](21-feature-pos-partida-gaps.md). Índice único em `SteamId`
(um usuário por conta Steam) e índice não-único em `Nickname` (para busca).

### `teams`
Índice único em `Tag` (a sigla do time, ex. "NAVI", nunca duplicada no sistema todo).
`CaptainId` é redundante com `User.TeamId + TeamRole.Captain` — o dono "de verdade" é sempre
quem tem `TeamRole.Captain` entre os membros; `Team.CaptainId` é mantido em sincronia
manualmente toda vez que a propriedade muda (transferência, saída do dono) mas não tem
constraint de banco garantindo essa consistência. Isso é um ponto de atenção: um bug futuro que
esqueça de atualizar `Team.CaptainId` num fluxo novo criaria uma divergência silenciosa entre
"quem o time diz que é dono" e "quem realmente tem o cargo".

### `teaminvitations`
Convite unidirecional criado pelo dono para um jogador específico. `Status` é o enum
`TeamInvitationStatus` (Pending/Accepted/Declined/Cancelled). Índice composto
`(TeamId, InvitedUserId)` acelera a checagem de "já existe convite pendente desse time para essa
pessoa?".

### `teamjoinrequests`
O espelho inverso do convite: o jogador pede para entrar, o dono aceita/recusa. Mesmos quatro
status mais `Expired` (`JoinRequestStatus`, definido em `Models/Competition.cs`) — embora hoje
nada no código automaticamente marque uma solicitação como `Expired`; esse valor existe no enum
mas não tem produtor ainda.

### `friendships`
Uma linha por par de usuários, direção fixada em `RequesterId`/`AddresseeId` mesmo depois de
aceita — index único `(RequesterId, AddresseeId)` impede duplicata **nessa ordem específica**,
mas a query de relação (`GET /api/friends/relation`) sempre checa os dois sentidos
(`(Requester=A,Addressee=B) OR (Requester=B,Addressee=A)`) porque uma amizade entre A e B pode
ter sido criada por qualquer um dos dois. `Status = Blocked` reaproveita a mesma tabela e o mesmo
enum (`FriendshipStatus`) em vez de criar uma tabela de bloqueio separada — ver
[Capítulo 14](14-feature-amizades.md) para os detalhes dessa decisão.

### `tournaments`
A configuração completa de um campeonato — nome, formato, datas, regras de série (MD1/MD3/MD5),
map pool como `MapPoolCsv` (uma string `"Mirage, Inferno, Nuke"`, nunca uma tabela separada de
mapas — ver 4.5). Não tem chave estrangeira nenhuma (é a raiz da árvore de campeonato).

### `tournamentteams`
A tabela de junção entre `tournaments` e `teams` — mas carrega muito mais que uma junção simples:
`Seed` (posição no sorteio), `IsEliminated`, `FinalPosition`, o status de `CheckIn`, e
`CaptainUserId` (o capitão *da escalação*, um conceito diferente do dono do time — ver
[Capítulo 17](17-feature-escalacao.md)). Índice único `(TournamentId, TeamId)` — um time só pode
se inscrever uma vez no mesmo campeonato.

### `tournamentlineupplayers`
Junção simples entre `tournamentteams` e `users`: quem está na escalação daquele time, naquele
campeonato específico. Índice único `(TournamentTeamId, UserId)`.

### `bracketrounds` / `bracketmatches`
Uma rodada (`bracketrounds`) tem N partidas (`bracketmatches`). `RoundNumber` não é
necessariamente sequencial visualmente — é usado como namespace para diferenciar Upper
(1, 2, 3...), Lower (101, 102...) e Grande Final (200) dentro do mesmo campeonato, todos
ordenáveis por esse número. `Side` (adicionado depois, via `ALTER TABLE`) diz a qual das três
subchaves a rodada pertence. `BracketMatch.MatchId` é o link opcional para a tabela `matches`
(só existe depois que o veto termina e a sala é criada). Ver
[Capítulo 18](18-feature-bracket.md) para a lógica completa de geração.

### `matches` / `matchplayers`
`matches` é tanto a "sala pré-jogo" (com `ServerIp`/`ServerPassword`/`ProvisionState` enquanto o
servidor ainda está subindo) quanto o "resultado pós-jogo" (`ScoreA`/`ScoreB`,
`Status = Finished`) — o mesmo registro serve às duas fases da vida de uma partida, sem tabela
separada para cada uma. `matchplayers` guarda o scoreboard por jogador (kills, deaths, HS%,
rating, MVP), com índice único `(MatchId, UserId)`.

### `badges` / `userbadges`
`badges` é o catálogo (fixo, vem do `SeedData`); `userbadges` é quais o usuário desbloqueou e
quando. Hoje `userbadges` só é populado manualmente pelo seed — não existe lógica de "conceder
badge quando X acontece" (ver [Capítulo 21](21-feature-pos-partida-gaps.md)).

### `vetosessions` / `vetosteps`
`vetosessions`: uma por `BracketMatch` (índice único em `BracketMatchId` — só pode haver um veto
por partida). `vetosteps`: cada ban/pick/decider individual, ordenado por `Order`, índice único
`(SessionId, Order)` — a sequência nunca pode ter dois passos com o mesmo número dentro da mesma
sessão. Ver [Capítulo 19](19-feature-veto.md).

### `auditlogs`
Log de auditoria, sem chave estrangeira para nada — grava ids como texto solto
(`ActorUserId`, `TargetUserId`, `TeamId`, `TournamentId` são todos `varchar`/`longtext`
sem `FOREIGN KEY`). Essa é uma escolha deliberada: um log de auditoria deve sobreviver mesmo que
a entidade referenciada seja excluída depois (você quer conseguir ver "fulano excluiu o time X"
mesmo que o time X já não exista mais no banco) — se fossem FKs de verdade, isso quebraria ou
exigiria `ON DELETE SET NULL` em tudo.

### `poolservers`
Sem nenhuma FK — é conceitualmente independente do resto do domínio (representa infraestrutura,
não dado de produto). `CurrentMatchId` é apenas um id de texto solto pelo mesmo motivo do log de
auditoria: um servidor de pool deve continuar existindo e sendo rastreável mesmo que a partida
associada já tenha terminado e potencialmente sido limpa.

## 4.5 Um padrão recorrente: listas guardadas como CSV, não como tabela filha

Repare em dois campos: `Tournament.MapPoolCsv` (`"Mirage, Inferno, Nuke, Ancient..."`) e
`VetoSession.MapPoolCsv`. Em vez de uma tabela `tournament_maps` com uma linha por mapa, o pool
inteiro é guardado como uma única string separada por vírgula, e convertido para
`List<string>` em uma propriedade computada (ignorada pelo EF Core, ver §4.3.2):

```csharp
public List<string> MapPool => string.IsNullOrWhiteSpace(MapPoolCsv)
    ? new List<string>()
    : MapPoolCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
```

Essa é uma simplificação deliberada: o map pool nunca precisa ser consultado individualmente
(`WHERE algum_mapa = 'Mirage'` nunca acontece em lugar nenhum do código) — ele só é lido inteiro,
sempre junto, então uma tabela filha normalizada adicionaria uma junção sem nenhum benefício de
consulta. Se um dia o produto precisar, por exemplo, filtrar campeonatos por "contém o mapa X",
essa modelagem precisaria mudar para uma tabela de fato.

## 4.6 Cuidado ao mexer em `enum`s persistidos

Como os enums são gravados como `int` (a posição do membro, não o nome), **a ordem dos membros
importa e nunca deve ser reordenada** depois que existem dados gravados. Por exemplo:

```csharp
public enum TeamRole { Member = 0, ViceCaptain = 1, Captain = 2 }
```

Se alguém reordenasse para `{ Captain = 0, ViceCaptain = 1, Member = 2 }`, todo `TeamRole` já
gravado no banco passaria a significar outra coisa (um dono viraria membro comum silenciosamente,
sem nenhum erro visível). A prática segura ao adicionar um novo valor é sempre **acrescentar no
fim** (ou atribuir o número explicitamente, como já é feito em todos os enums deste projeto —
repare que todo `enum` do `Models/` já declara os valores numéricos explicitamente, ex.
`Pending = 0, Accepted = 1, ...`, exatamente para tornar esse risco visível e intencional em vez
de implícito).

## 4.7 Como recriar o banco do zero (fluxo de dev)

```bash
mysql -u root -e "CREATE DATABASE summit CHARACTER SET utf8mb4"
mysql -u root summit < database/schema.sql
```

Isso aplica o dump de referência. Na prática, durante desenvolvimento normal, basta apagar o
banco (`DROP DATABASE summit;`) e deixar a própria API recriá-lo via `EnsureCreated()` na
próxima subida — o `schema.sql` é útil principalmente como **documentação legível** do estado do
schema e como forma de recriar rapidamente sem subir a API primeiro. Ele não é regenerado
automaticamente quando o `ApiDbContext` muda — se você adicionar uma tabela/coluna, o
`schema.sql` do repositório fica desatualizado até alguém rodar um novo `mysqldump --no-data` e
substituir o arquivo manualmente.
