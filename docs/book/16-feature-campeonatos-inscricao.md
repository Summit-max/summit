[← Sumário](00-indice.md)

# Capítulo 16 — Campeonatos: Inscrição e Check-in

## 16.1 A linha do tempo automática de um campeonato

Tudo em torno do ciclo de vida de um campeonato gira em torno de três datas **calculadas**, nunca
digitadas por um organizador — são todas derivadas de `Tournament.StartDate` (`Models/Tournament.cs`):

```csharp
public DateTime RegistrationClosesAt => StartDate.AddHours(-12);
public DateTime CheckInOpensAt       => StartDate.AddHours(-1);
public DateTime CheckInClosesAt      => StartDate.AddMinutes(-30);
```

| Momento | O que acontece | Quem dispara |
|---|---|---|
| Publicação | Inscrições abrem | (implícito — o campeonato já nasce `Open`) |
| T-12h | Inscrições fecham | checagem em `POST /api/tournaments/{id}/register` (recusa se `now >= RegistrationClosesAt`) |
| T-1h | Check-in abre | checagem em `POST /api/tournaments/{id}/checkin` (recusa se `now < CheckInOpensAt`) — e também é o instante em que `CanEditLineup` vira `false` (ver Capítulo 17) |
| T-30min | Check-in fecha; ausentes removidos; chave gerada | `LifecycleWorker.TickAsync`, ramo `now >= t.CheckInClosesAt` |
| T-0 (`StartDate`) | Campeonato inicia; vetos da 1ª rodada abrem | `LifecycleWorker.TickAsync`, ramo `now >= t.StartDate` |

Não existe nenhum job agendado externo (cron, Hangfire, Quartz) — tudo é resolvido pelo
`LifecycleWorker` comparando `DateTime.UtcNow` contra essas propriedades computadas a cada tick de
20 segundos (ver [§11.2](11-backend-services-workers.md#112-lifecycleworker--estrutura-de-código)).
Isso significa que a precisão do sistema é de "até 20 segundos de atraso" em relação ao horário
exato — aceitável para este domínio (ninguém precisa que o check-in feche no milissegundo exato).

## 16.2 Inscrição

A rota mais densa de validações do projeto já foi detalhada em
[§10.3.1](10-backend-endpoints.md#1031-inscrição-a-rota-mais-densa-de-regras-do-projeto). Do lado
do client, `TournamentsViewModel.RegisterAsync` chama isso sem nenhuma tela de "escolher
escalação agora" — o time do usuário atual é usado direto (`App.UserService.CurrentUser?.TeamId`),
e a escalação é resolvida pelo fallback automático da API (os 5 membros mais antigos). Ajustar
quem realmente joga acontece depois, na tela dedicada de Escalação, a qualquer momento até o
check-in abrir — ver [Capítulo 17](17-feature-escalacao.md).

Idempotência é tratada explicitamente: chamar `register` para um time já inscrito **não é um
erro**, simplesmente devolve sucesso sem duplicar nada:

```csharp
var exists = await db.TournamentTeams.AnyAsync(x => x.TournamentId == id && x.TeamId == req.TeamId);
if (exists) return Results.Ok(true);
```

## 16.3 Check-in

O check-in confirma presença de um time já inscrito, dentro da janela T-1h → T-0. Quem pode
confirmar não é só "dono ou sublíder do time" de forma genérica — a regra é mais específica e
vale a pena ler com atenção:

```csharp
var canCheckIn = by != null &&
    ((by.TeamId == body.TeamId && (by.TeamRole == TeamRole.Captain || by.TeamRole == TeamRole.ViceCaptain))
     || by.Id == tt.CaptainUserId);
```

Ou seja: **dono ou sublíder do time em geral**, **ou** especificamente o **capitão da escalação
daquele campeonato** (mesmo que essa pessoa seja um membro comum sem cargo administrativo no
time) — reflete `docs/espec-campeonatos.md §4`: "(dono, sublíder ou capitão da escalação, se
habilitado no campeonato)". Isso é a primeira vez neste livro em que os dois conceitos de
"capitão" (dono do time vs. capitão da escalação, ver [§13.1](13-feature-times.md#131-cargos--o-vocabulário))
se encontram na mesma regra de permissão — e é exatamente por isso que a distinção entre os dois
importa tanto: um membro comum, sem nenhum cargo administrativo no time, pode legitimamente
confirmar o check-in de todo o time se ele foi escolhido como capitão da escalação daquele
campeonato específico.

Antes de confirmar, a rota **revalida os 5 da escalação inteira**:

```csharp
var lineupIds = tt.Lineup.Select(l => l.UserId).ToList();
var stillValid = await db.Users.CountAsync(u => lineupIds.Contains(u.Id) && u.TeamId == body.TeamId);
if (lineupIds.Count != 5 || stillValid != 5)
    return Results.BadRequest("Escalação inválida: o time precisa de 5 jogadores elegíveis.");
```

Isso cobre o cenário em que um jogador da escalação saiu do time (ou foi removido) depois da
inscrição mas antes do check-in — `stillValid` conta só quem ainda pertence ao time *agora*; se
algum saiu, a contagem cai abaixo de 5 e o check-in é recusado até a escalação ser corrigida
(reforça `docs/espec-times.md §18`: "jogador sai antes do bloqueio → removido da escalação,
inscrição incompleta, notificar").

## 16.4 Fechamento automático do check-in (T-30min)

```csharp
// Summit.Api/LifecycleWorker.cs
if (now >= t.CheckInClosesAt)
{
    var waiting = t.TournamentTeams.Where(x => x.CheckIn == CheckInStatus.Waiting && !x.IsEliminated).ToList();
    foreach (var w in waiting)
    {
        w.CheckIn = CheckInStatus.NoShow;
        w.IsEliminated = true;
        await CompetitionEndpoints.Audit(db, "team_noshow_removed", ...);
    }
    if (t.Bracket.Count == 0) { /* gera a chave só com os CONFIRMADOS */ }
}
```

Todo time que ainda está `CheckInStatus.Waiting` nesse momento vira `NoShow` e é marcado
`IsEliminated = true` — automaticamente, sem intervenção humana. A geração da chave (ver
[Capítulo 18](18-feature-bracket.md)) só acontece depois disso, e só considera
`x.CheckIn == CheckInStatus.Confirmed` — times ausentes nunca entram na chave.

Existe também um endpoint equivalente chamável manualmente,
`POST /api/tournaments/{id}/close-checkin`, com a mesma lógica de remoção — mas ele não gera a
chave sozinho (só o `LifecycleWorker` faz as duas coisas em sequência); esse endpoint existe
principalmente como ferramenta administrativa/de teste, não como parte do fluxo automático normal.

## 16.5 O fallback do T-0: e se ninguém fez check-in?

```csharp
if (now >= t.StartDate)
{
    if (t.Bracket.Count == 0)   // chave ainda não foi gerada (ex.: check-in nunca rodou, ou pulou)
    {
        var teams = t.TournamentTeams.Where(x => !x.IsEliminated && x.Team != null).OrderBy(x => x.Seed).ToList();
        if (teams.Count >= 2) GenerateBracket(db, t, teams);
    }
    if (t.Bracket.Count == 0) continue;   // ainda sem times suficientes: aguarda
    // ...
}
```

Esse é um caminho de recuperação: se por algum motivo a chave ainda não existe quando
`StartDate` chega (por exemplo, o processo da API ficou parado durante toda a janela de check-in
— algo que de fato aconteceu várias vezes durante este projeto, dado os intervalos longos entre
sessões de desenvolvimento), o `LifecycleWorker` gera a chave na hora, usando **todos os inscritos
não eliminados** em vez de só os confirmados (já que nenhum check-in rodou para filtrar quem
faltou). Se mesmo assim não houver pelo menos 2 times, o campeonato simplesmente aguarda
(`continue`) — ele não trava em erro, só permanece sem iniciar até algo mudar (por exemplo, mais
uma equipe se inscrever, embora tecnicamente as inscrições já devessem estar fechadas há muito
tempo nesse ponto).

## 16.6 O que falta neste domínio

A especificação (`docs/espec-campeonatos.md`) prevê ações administrativas — desclassificação,
remarcação, W.O. por não comparecer *no servidor* (diferente do no-show do check-in, que já
existe) — que não estão implementadas. Isso está detalhado como parte do gap maior no
[Capítulo 21](21-feature-pos-partida-gaps.md), já que a maioria dessas ações depende de haver
primeiro um conceito de "resultado de partida" que hoje não existe.
