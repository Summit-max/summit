[← Sumário](00-indice.md)

# Capítulo 18 — Chave (Bracket)

Este é o capítulo mais matemático do livro — a geração e o desenho da chave envolvem fórmulas que
vale a pena entender passo a passo, não só citar.

## 18.1 O vocabulário: `BracketRound`, `BracketMatch`, `BracketSide`

```csharp
public enum BracketMatchStatus { Pending = 0, Live = 1, Finished = 2, Veto = 3, PreparingServer = 4 }
public enum BracketSide { Upper = 0, Lower = 1, GrandFinal = 2 }

public class BracketRound
{
    public int RoundNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public BracketSide Side { get; set; } = BracketSide.Upper;
    public List<BracketMatch> Matches { get; set; } = new();
}
```

`BracketSide` só importa para eliminação **dupla** — eliminação simples usa exclusivamente
`Upper`. `RoundNumber` não é um índice visual sequencial: é usado como **namespace** para separar
as três sub-chaves dentro do mesmo campeonato — Upper usa 1, 2, 3...; Lower usa 101, 102...
(offset `100 + i`); Grande Final usa exatamente `200`. Isso permite que uma consulta simples
(`ORDER BY RoundNumber`) sempre ordene corretamente dentro de cada sub-chave, sem precisar de uma
segunda coluna de "ordem visual".

## 18.2 Geração — eliminação simples

`LifecycleWorker.GenerateSingleElimination` (privado, mas reusado tanto para uma chave puramente
simples quanto como a metade "Upper" de uma chave dupla):

```csharp
teams = teams.OrderBy(_ => Random.Shared.Next()).ToList();   // seed ALEATÓRIO
int n = teams.Count;
int totalRounds = (int)Math.Ceiling(Math.Log2(Math.Max(n, 2)));

for (int r = 0; r < totalRounds; r++)
{
    int matchesInRound = (int)Math.Ceiling(n / Math.Pow(2, r + 1));
    for (int p = 0; p < Math.Max(matchesInRound, 1); p++)
    {
        // só a rodada 0 recebe times de verdade; o resto nasce "TBD"
        if (r == 0)
        {
            var a = p * 2 < n ? teams[p * 2] : null;
            var b = p * 2 + 1 < n ? teams[p * 2 + 1] : null;
            bm.TeamATag = a?.Team?.Tag ?? "TBD";
            bm.TeamBTag = b?.Team?.Tag ?? "BYE";
        }
    }
}
```

Dois pontos centrais:

1. **O sorteio é aleatório** (`docs/espec-campeonatos.md §5` permite manual/ranking/aleatório —
   hoje só o modo aleatório está implementado; não há UI para o organizador escolher seed manual
   ou por ranking).
2. **Só a primeira rodada recebe nomes de times reais.** Todas as rodadas seguintes nascem com
   `TeamATag = TeamBTag = "TBD"` — porque preencher quem avança depende de saber o resultado da
   partida anterior, e **isso ainda não existe no sistema** (ver
   [Capítulo 21](21-feature-pos-partida-gaps.md)). Isso significa: **a chave, hoje, só é
   realmente jogável na primeira rodada.** As rodadas seguintes ficam visualmente montadas
   (a estrutura de colunas/partidas existe) mas travadas em "TBD" para sempre, até que alguém
   preencha manualmente ou até que o avanço automático seja implementado.
3. **BYE**, não "TBD", para o lado B quando o número de times é ímpar/não é potência de 2 exata:
   `p * 2 + 1 < n ? teams[p*2+1] : null` seguido de `b?.Team?.Tag ?? "BYE"` — um time sem
   adversário na primeira rodada aparece com `"BYE"` no lado oposto, não com `"TBD"` (a diferença
   textual comunica corretamente "não existe adversário aqui", vs. "existe adversário, mas ainda
   não sabemos quem").

Nomeação de rodada (`RoundName`) é dinâmica, calculada de trás para frente a partir do total de
rodadas: a última é sempre `"FINAL"`, a penúltima `"SEMIS"`, a antepenúltima `"QUARTAS"`, e
qualquer coisa antes disso vira `"RODADA {n}"` genérico — o que garante nomes corretos
independente de quantos times existem (8 times = 3 rodadas = Quartas/Semis/Final; 16 times = 4
rodadas = Oitavas seria "RODADA 1"/Quartas/Semis/Final, já que o código não tem um nome
específico para oitavas).

## 18.3 Geração — eliminação dupla

`LifecycleWorker.GenerateDoubleElimination` monta três blocos em sequência:

```csharp
int n = teams.Count;
int k = (int)Math.Ceiling(Math.Log2(Math.Max(n, 2)));

GenerateSingleElimination(db, t, teams, BracketSide.Upper);   // reaproveita o algoritmo acima

int lowerRounds = Math.Max(0, 2 * (k - 1));
for (int i = 0; i < lowerRounds; i++)
{
    int matchesInRound = Math.Max(1, (int)(n / Math.Pow(2, Math.Floor(i / 2.0) + 2)));
    // rodada i: RoundNumber = 100+i+1, Name = "LOWER {i+1}" ou "LOWER FINAL" na última
    // TODAS as partidas nascem TBD/TBD (nem a "primeira rodada" da Lower tem time real —
    // não há como saber quem cai pra lá sem o avanço da Upper, que não existe ainda)
}

// Grande Final: 1 rodada, 1 partida, RoundNumber=200, Side=GrandFinal, times TBD
```

### 18.3.1 A fórmula de contagem de partidas por rodada da Lower

```csharp
int matchesInRound = Math.Max(1, (int)(n / Math.Pow(2, Math.Floor(i / 2.0) + 2)));
```

Essa fórmula foi **verificada empiricamente ao vivo** (via o endpoint de debug
`/api/debug/generate-bracket/{tournamentId}`, ver [§9.4](09-backend-api-program.md#94-os-endpoints-de-diagnóstico-apidebug))
contra dois casos concretos:

- **N=4 (k=2)**: `lowerRounds = 2*(2-1) = 2`. Rodada 0 (`i=0`): `4 / 2^(0+2) = 4/4 = 1` partida
  ("LOWER 1"). Rodada 1 (`i=1`, última): `4 / 2^(0+2) = 1` partida ("LOWER FINAL"). Total: Upper
  (2 partidas na rodada 1 + 1 na final = 3) + Lower (1+1=2) + Grande Final (1) = **6 partidas**,
  que bate exatamente com a fórmula geral de eliminação dupla `2N-2` para N=4 (`2*4-2=6`). ✓
- **N=7 (single elimination)**: testado separadamente, confirmando o cálculo correto de BYE
  (um time sem adversário na primeira rodada) quando N não é potência de 2 exata.

Vale registrar isso porque a fórmula em si não é óbvia de derivar de cabeça — ela existe
justamente para replicar a estrutura padrão de dupla eliminação (a Lower "absorve" os perdedores
da Upper em um ritmo específico onde cada duas rodadas da Lower processam uma rodada de
eliminados da Upper), e foi mantida como está desde que a verificação empírica confirmou que
bate com `2N-2` para os casos testados.

### 18.3.2 Por que a Lower inteira nasce "TBD" (diferente da Upper, que ganha a rodada 1 preenchida)

Isso é intencional e consistente com o resto do sistema: preencher a Lower exigiria saber quem
perdeu cada partida da Upper — informação que só existiria se houvesse avanço automático de
resultado (o gap do [Capítulo 21](21-feature-pos-partida-gaps.md)). Então, diferente da Upper
(que pelo menos consegue preencher sua *primeira* rodada com o sorteio inicial), a Lower inteira
— mesmo sua "primeira" rodada — fica travada em TBD até esse gap ser resolvido.

`Tournament.BracketReset` (um campo booleano já existente no modelo, pensado para a "partida de
reset" da grande final quando o campeão vindo da Lower vence a primeira série) também não é usado
ainda — a mesma razão: essa mecânica só faz sentido quando existe avanço de resultado de verdade.

### 18.3.3 Sistema Suíço: fora de escopo, documentado

```csharp
internal static void GenerateBracket(ApiDbContext db, Tournament t, List<TournamentTeam> teams)
{
    if (t.FormatType == TournamentFormat.DoubleElimination)
        GenerateDoubleElimination(db, t, teams);
    else
        GenerateSingleElimination(db, t, teams, BracketSide.Upper);
}
```

Repare que não há nenhum `case` para `TournamentFormat.Swiss` — qualquer valor que não seja
`DoubleElimination` cai no `else` de eliminação simples. Isso significa que, hoje, **configurar
um campeonato como Suíço no banco produziria uma chave de eliminação simples por engano** (não um
erro, um comportamento silenciosamente errado) — porque não existe nenhuma UI no client para
criar um campeonato e escolher o formato (todos os campeonatos hoje vêm do `SeedData`, todos
`SingleElimination` ou `DoubleElimination`), esse caso nunca foi de fato exercitado. É uma
lacuna a ter em mente se um dia a criação de campeonatos ganhar UI própria com essa opção
disponível.

## 18.4 `BracketLayout` — o algoritmo de desenho genérico

`ViewModels/BracketLayout.cs` resolve um problema puramente visual: como desenhar N colunas
(rodadas) com um número decrescente de partidas por coluna, mantendo o alinhamento vertical
correto (cada partida da rodada 2 centralizada entre as duas partidas da rodada 1 que a
alimentam), **sem desenhar nenhuma linha de conexão** (decisão de design consciente — ver
[§18.4.2](#1842-por-que-sem-linhas-conectoras)) e **para qualquer quantidade de times**.

```csharp
public static List<BracketColumnViewModel> Build(IEnumerable<BracketRound> rounds, double unit = 96, double cardHeight = 80)
{
    var ordered = rounds.OrderBy(r => r.RoundNumber).ToList();
    var columns = new List<BracketColumnViewModel>();

    for (int r = 0; r < ordered.Count; r++)
    {
        var matches = ordered[r].Matches.OrderBy(m => m.Position).ToList();
        var spacing = unit * Math.Pow(2, r);
        var slots = matches.Select((m, i) => new BracketSlotViewModel
        {
            Match = m,
            Top = spacing * i + (spacing - unit) / 2
        }).ToList();

        var height = slots.Count == 0 ? cardHeight : slots.Max(s => s.Top) + unit;
        columns.Add(new BracketColumnViewModel { Header = ordered[r].Name, Slots = slots, Height = height });
    }
    return columns;
}
```

### 18.4.1 A matemática do espaçamento

`unit` (96px por padrão) é a altura "lógica" reservada para cada partida na primeira rodada
(card de 80px + um respiro de 16px). Em cada rodada seguinte, o espaço vertical entre partidas
**dobra** (`spacing = unit * 2^r`) — porque cada partida da rodada `r` precisa ocupar
verticalmente o mesmo espaço que as *duas* partidas da rodada `r-1` que a alimentam ocupavam
juntas. O deslocamento `(spacing - unit) / 2` centraliza o card all width `unit` dentro desse
espaço maior `spacing`, para que ele fique visualmente no meio das duas partidas anteriores — este
é exatamente o efeito visual de uma chave eliminatória clássica, só que calculado
matematicamente em vez de fixado à mão.

**Exemplo genérico** para tornar a fórmula concreta (rodada `r=1`, segunda coluna, `unit=96`):
`spacing = 96 * 2^1 = 192`. Partida de índice `i=0`: `Top = 192*0 + (192-96)/2 = 48`. Partida
`i=1`: `Top = 192*1 + 48 = 240`. Ou seja, a primeira partida da segunda rodada fica centralizada
entre as posições `0` e `96` da primeira rodada (a média é `48` ✓), e a segunda partida da segunda
rodada fica centralizada entre `192` e `288` (a média é `240` ✓).

### 18.4.2 Por que sem linhas conectoras

Essa foi uma escolha explícita, feita via `AskUserQuestion` durante o desenvolvimento: desenhar
as linhas de conexão entre uma partida e a próxima exigiria calcular geometria de `Path` (pontos
de entrada/saída, curvas) que varia com o número de times e a posição relativa de cada partida —
uma complexidade bem maior do que o espaçamento vertical sozinho. A alternativa escolhida
("colunas com espaçamento padrão de bracket, sem linhas conectoras") é visualmente mais simples
mas generaliza para *qualquer* tamanho de chave com uma fração do código.

## 18.5 O XAML: de hardcoded para genérico

Antes desta refatoração, `TournamentDetailsView.xaml` tinha um bloco fixo — um `Canvas` de
840×384 pixels com posições `Canvas.Left`/`Canvas.Top` **literais** para 7 partidas
(assumindo sempre exatamente 8 times, eliminação simples), mais uma `Path` desenhada à mão como
conector entre elas. Hoje esse bloco inteiro foi substituído por um `DataTemplate` reusável:

```xml
<DataTemplate x:Key="BracketColumnTpl">
    <StackPanel Margin="0,0,40,0" Width="240">
        <TextBlock Text="{Binding Header}" .../>
        <ItemsControl ItemsSource="{Binding Slots}" Width="240" Height="{Binding Height}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><Canvas/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemContainerStyle>
                <Style TargetType="ContentPresenter">
                    <Setter Property="Canvas.Top" Value="{Binding Top}"/>
                    <Setter Property="Canvas.Left" Value="0"/>
                </Style>
            </ItemsControl.ItemContainerStyle>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Width="240" Height="80" Style="{StaticResource MatchCard}">
                        <ContentControl Content="{Binding Match}" ContentTemplate="{StaticResource MatchInnerTpl}"/>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</DataTemplate>
```

O truque técnico central aqui é `ItemsControl.ItemsPanel` trocado para `Canvas` (em vez do
`StackPanel` padrão), combinado com `ItemContainerStyle` fazendo `Setter Property="Canvas.Top"
Value="{Binding Top}"` — isso funciona porque `Canvas.Top` é uma *propriedade anexada*
(`attached property`) que aceita um `double` direto via binding, sem precisar de nenhum
`IValueConverter`. É esse binding que aplica, item por item, os valores calculados por
`BracketLayout.Build` (a propriedade `Top` de cada `BracketSlotViewModel`).

Esse `DataTemplate` é reaproveitado **três vezes** na view (uma para Upper, uma para Lower, uma
para Grande Final), cada uma ligada à propriedade computada correspondente do
`TournamentDetailsViewModel`:

```csharp
public List<BracketColumnViewModel> UpperColumns =>
    BracketLayout.Build(Tournament?.Bracket.Where(r => r.Side == BracketSide.Upper) ?? Enumerable.Empty<BracketRound>());
public List<BracketColumnViewModel> LowerColumns =>
    BracketLayout.Build(Tournament?.Bracket.Where(r => r.Side == BracketSide.Lower) ?? Enumerable.Empty<BracketRound>());
public bool HasLowerBracket => LowerColumns.Count > 0;
```

A seção "CHAVE INFERIOR" (Lower) e a Grande Final só ficam visíveis quando `HasLowerBracket` é
verdadeiro — o que automaticamente cobre o caso de eliminação simples (sem nenhum `if` explícito
de "é simples ou dupla?" no XAML: se não há rodadas `Lower`, a lista fica vazia, e a seção some
sozinha por causa do binding de `Visibility`).

## 18.6 Como testar isto sem esperar os horários automáticos

O endpoint `POST /api/debug/generate-bracket/{tournamentId}` (ver
[§9.4](09-backend-api-program.md#94-os-endpoints-de-diagnóstico-apidebug)) limpa qualquer chave
existente daquele campeonato e chama `LifecycleWorker.GenerateBracket` diretamente — permitindo
testar a geração (simples ou dupla, qualquer contagem de times inscritos) sem precisar que
`CheckInClosesAt` chegue de verdade. Foi assim que os casos N=4 e N=7 citados em
[§18.3.1](#1831-a-fórmula-de-contagem-de-partidas-por-rodada-da-lower) foram verificados.
