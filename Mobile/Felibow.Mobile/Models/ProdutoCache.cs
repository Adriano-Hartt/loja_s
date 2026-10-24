using SQLite;

namespace Felibow.Mobile.Models;

/// <summary>Cópia local do catálogo para uso offline (tabela SQLite do aparelho).</summary>
public class ProdutoCache
{
    [PrimaryKey] public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Descricao { get; set; } = "";
    public decimal Preco { get; set; }
    public decimal PrecoEfetivo { get; set; }
    public int Estoque { get; set; }
    public string? ImagemUrl { get; set; }
    public string? EdicaoEspecial { get; set; }
    public DateTime AtualizadoEm { get; set; }

    public static ProdutoCache De(ProdutoDto p) => new()
    {
        Id = p.Id, Nome = p.Nome, Sku = p.Sku, Descricao = p.Descricao, Preco = p.Preco,
        PrecoEfetivo = p.PrecoEfetivo, Estoque = p.Estoque, ImagemUrl = p.ImagemUrl,
        EdicaoEspecial = p.EdicaoEspecial, AtualizadoEm = DateTime.UtcNow
    };

    public ProdutoDto ParaDto() => new(Id, Nome, Sku, Descricao, Preco, PrecoEfetivo < Preco ? PrecoEfetivo : null,
        PrecoEfetivo, Estoque, ImagemUrl, EdicaoEspecial, null);
}
