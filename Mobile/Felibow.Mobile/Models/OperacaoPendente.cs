using SQLite;

namespace Felibow.Mobile.Models;

public enum TipoOperacao
{
    Favoritar = 1,
    Desfavoritar = 2,
    AdicionarAoCarrinho = 3
}

/// <summary>Ação feita sem rede, guardada no SQLite do aparelho até ser enviada à API.</summary>
public class OperacaoPendente
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public TipoOperacao Tipo { get; set; }
    public int ProdutoId { get; set; }
    public int Quantidade { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
    public int Tentativas { get; set; }
}

public class FavoritoCache
{
    [PrimaryKey] public int ProdutoId { get; set; }
}
