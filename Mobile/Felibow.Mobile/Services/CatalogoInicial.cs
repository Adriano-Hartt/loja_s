using Felibow.Mobile.Models;

namespace Felibow.Mobile.Services;

/// <summary>Catalogo de demonstracao embutido: exibido quando o servidor ainda nao respondeu e nao ha cache.</summary>
public static class CatalogoInicial
{
    private static ProdutoDto P(int id, string nome, string sku, decimal preco, int estoque, string img) =>
        new(id, nome, sku, "Suplemento alimentar Felibow.", preco, null, preco, estoque,
            "~/img/produtos/" + img + ".png", null, nome.Split(" \u2014 ")[0]);

    public static readonly List<ProdutoDto> Todos = new()
    {
        P(1, "VitaPrime \u2014 Creatina Monohidratada 300 g", "PD-001", 149.90m, 24, "creatina"),
        P(2, "WheyCore \u2014 Whey Protein Chocolate 900 g", "DW-002", 169.90m, 18, "whey"),
        P(3, "MultiVida \u2014 Multivitam\u00ednico 60 c\u00e1psulas", "CE-003", 99.90m, 20, "multivitaminico"),
        P(4, "OceanPure \u2014 \u00d4mega 3 120 c\u00e1psulas", "BS-004", 129.90m, 14, "omega3"),
        P(5, "MagnaBalance \u2014 Magn\u00e9sio 60 c\u00e1psulas", "MB-005", 89.90m, 16, "magnesio"),
        P(6, "SolVita \u2014 Vitamina D3 + K2 60 c\u00e1psulas", "SP-006", 79.90m, 18, "d3k2"),
        P(7, "GlutaFlow \u2014 Glutamina 300 g", "MR-007", 109.90m, 12, "glutamina"),
        P(9, "DuoPower \u2014 Pr\u00e9-treino Frutas Vermelhas", "DP-009", 94.90m, 15, "duopower"),
        P(10, "TurboVolt \u2014 Pr\u00e9-treino Blueberry", "TV-010", 99.90m, 15, "turbovolt"),
    };

    public static List<ProdutoDto> Buscar(string? busca) =>
        string.IsNullOrWhiteSpace(busca)
            ? Todos.ToList()
            : Todos.Where(p => p.Nome.Contains(busca, StringComparison.OrdinalIgnoreCase)).ToList();
}