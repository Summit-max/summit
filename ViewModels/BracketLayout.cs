using Summit.Models;

namespace Summit.ViewModels;

public class BracketSlotViewModel
{
    public BracketMatch? Match { get; set; }
    public double Top { get; set; }
}

public class BracketColumnViewModel
{
    public string Header { get; set; } = string.Empty;
    public List<BracketSlotViewModel> Slots { get; set; } = new();
    public double Height { get; set; }
}

/// <summary>
/// Layout genérico de bracket: colunas por rodada, espaçamento vertical padrão (dobra a cada
/// rodada), sem linhas conectoras — troca o desenho hardcoded de 8 times por algo que funciona
/// pra qualquer quantidade/formato (docs/pendencias.md §5).
/// </summary>
public static class BracketLayout
{
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

            var height = slots.Count == 0
                ? cardHeight
                : slots.Max(s => s.Top) + unit;

            columns.Add(new BracketColumnViewModel
            {
                Header = ordered[r].Name,
                Slots = slots,
                Height = height
            });
        }

        return columns;
    }
}
