[← Sumário](00-indice.md)

# Capítulo 15 — Auditoria

## 15.1 Propósito e forma

`AuditLog` (`Models/Competition.cs`) é um registro de texto livre, sem chave estrangeira real
para nada (ver [§4.4](04-banco-dados.md#44-tabela-por-tabela), seção `auditlogs`):

```csharp
public class AuditLog
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? TeamId { get; set; }
    public string? TournamentId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Essa forma "achatada" (todos os campos opcionais, preenchidos ou não conforme o tipo de ação) é
deliberada: um único tipo de registro serve para qualquer ação auditável do sistema, sem precisar
de uma tabela por tipo de evento. O preço dessa simplicidade é que **nada garante em nível de
banco** que `TeamId` esteja preenchido quando `Action` é algo relacionado a time — essa
consistência é responsabilidade de quem chama `Audit(...)` em cada endpoint, não do schema.

## 15.2 Catálogo de ações auditadas hoje

Toda chamada a `CompetitionEndpoints.Audit(...)` no código hoje, por área:

| Área | Ações (`Action`) |
|---|---|
| Times | `team_edited`, `team_deleted`, `member_kicked`, `member_left`, `ownership_auto_transferred`, `ownership_transferred`, `member_promoted`, `member_demoted` |
| Solicitações de entrada | `join_request_created`, `join_request_accepted`, `join_request_declined` |
| Campeonatos | `team_registered`, `team_noshow_removed`, `bracket_generated`, `tournament_started`, `checkin_confirmed`, `lineup_changed` |
| Veto | `veto_completed` |

Note que **nem toda ação do sistema é auditada** — por exemplo, aceitar/recusar convite de time
(`POST /api/teams/invitations/{id}/accept`/`decline`) e as ações de amizade (Capítulo 14) não
chamam `Audit`. Isso não é uma inconsistência acidental relatada como bug em nenhum lugar do
projeto, mas é uma observação útil: **a cobertura de auditoria é parcial**, concentrada nas
ações que a especificação (`docs/espec-times.md §32`) explicitamente listou como merecedoras de
log ("criação/edição do time, entradas/saídas/remoções, convites, promoções, troca de dono,
inscrições, escalações, exclusão").

## 15.3 O helper `Audit` e por que ele não salva sozinho

```csharp
public static Task Audit(ApiDbContext db, string action, string? actor, string? target,
    string? teamId, string? tournamentId, string? oldValue, string? newValue, string? reason)
{
    db.AuditLogs.Add(new AuditLog { Id = $"aud_{Guid.NewGuid():N}", Action = action, /* ... */ });
    return Task.CompletedTask;
}
```

Já explicado em [§3.8](03-padroes-projeto.md#38-auditoria-como-efeito-colateral-padronizado):
`Audit` só adiciona a entidade ao `DbContext`, e o `SaveChangesAsync()` seguinte (do próprio
endpoint que chamou `Audit`) grava o log **na mesma transação** da mudança real. Isso garante
atomicidade: se o `SaveChangesAsync` falhar por qualquer motivo, nem a mudança nem o log são
persistidos — nunca existe um log de auditoria "órfão" descrevendo uma mudança que na verdade não
aconteceu.

`Audit` retorna `Task` (não é realmente assíncrono, sempre `Task.CompletedTask`) só para manter a
assinatura consistente com o resto do código async ao redor — pode ser chamado com `await` sem
custo real, mantendo a leitura do código uniforme.

## 15.4 A tela: somente leitura, por design

`AuditLogViewModel` aceita filtros opcionais por `teamId` e/ou `tournamentId` no construtor, e o
`AuditRepository` monta a query string condicionalmente:

```csharp
public async Task<List<AuditLog>> GetAsync(string? teamId = null, string? tournamentId = null, int take = 50)
{
    var qs = $"?take={take}" + (teamId != null ? $"&teamId={teamId}" : "") + (tournamentId != null ? $"&tournamentId={tournamentId}" : "");
    return await ApiClient.GetAsync<List<AuditLog>>($"/api/audit{qs}") ?? new();
}
```

A tela (`Views/AuditLogView.xaml`) não tem nenhum comando de ação além de "voltar" — é
propositalmente **read-only**: lista `Action`, `CreatedAt`, e o par `OldValue`→`NewValue` quando
presentes, mais `Reason` quando houver. Não há filtro de data, paginação além do `take` fixo
(100, passado por `AuditLogViewModel`), ou busca por ator — é a versão mínima de uma tela de
auditoria, suficiente para "o dono do time consegue ver o que aconteceu", mas sem nenhuma
ferramenta de investigação mais profunda.

Hoje o único ponto de entrada para essa tela é o botão "HISTÓRICO" em `TeamView.xaml`
(navegando com `teamId` preenchido). Não existe um ponto de entrada equivalente a partir de uma
tela de campeonato (mesmo `AuditLogViewModel` já aceitando `tournamentId` desde o construtor) —
seria um acréscimo pequeno e direto se o produto quisesse um "histórico do campeonato" visível
para o organizador.
