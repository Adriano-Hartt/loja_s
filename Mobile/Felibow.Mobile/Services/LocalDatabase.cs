using Felibow.Mobile.Models;
using SQLite;

namespace Felibow.Mobile.Services;

/// <summary>SQLite local do aparelho: cache do catálogo para navegar sem rede.</summary>
public class LocalDatabase
{
    private SQLiteAsyncConnection? _db;

    private async Task<SQLiteAsyncConnection> ConectarAsync()
    {
        if (_db != null) return _db;
        _db = new SQLiteAsyncConnection(Path.Combine(FileSystem.AppDataDirectory, "felibow_cache.db3"));
        await _db.CreateTableAsync<ProdutoCache>();
        await _db.CreateTableAsync<OperacaoPendente>();
        await _db.CreateTableAsync<FavoritoCache>();
        return _db;
    }

    public async Task SubstituirCatalogoAsync(IEnumerable<ProdutoDto> produtos)
    {
        var db = await ConectarAsync();
        await db.RunInTransactionAsync(c =>
        {
            c.DeleteAll<ProdutoCache>();
            c.InsertAll(produtos.Select(ProdutoCache.De));
        });
    }

    public async Task<List<ProdutoDto>> CatalogoAsync(string? busca)
    {
        var db = await ConectarAsync();
        var lista = await db.Table<ProdutoCache>().OrderBy(p => p.Nome).ToListAsync();
        if (!string.IsNullOrWhiteSpace(busca))
            lista = lista.Where(p => p.Nome.Contains(busca, StringComparison.CurrentCultureIgnoreCase)
                                     || p.Sku.Contains(busca, StringComparison.OrdinalIgnoreCase)).ToList();
        return lista.Select(p => p.ParaDto()).ToList();
    }

    public async Task<ProdutoDto?> ProdutoAsync(int id)
    {
        var db = await ConectarAsync();
        return (await db.FindAsync<ProdutoCache>(id))?.ParaDto();
    }

    public async Task<DateTime?> UltimaSincronizacaoAsync()
    {
        var db = await ConectarAsync();
        var p = await db.Table<ProdutoCache>().FirstOrDefaultAsync();
        return p?.AtualizadoEm;
    }

    // ---- fila de operações offline ----

    public async Task EnfileirarAsync(OperacaoPendente op)
    {
        var db = await ConectarAsync();
        await db.InsertAsync(op);
    }

    public async Task<List<OperacaoPendente>> PendentesAsync()
    {
        var db = await ConectarAsync();
        return await db.Table<OperacaoPendente>().OrderBy(o => o.Id).ToListAsync();
    }

    public async Task<int> ContarPendentesAsync()
    {
        var db = await ConectarAsync();
        return await db.Table<OperacaoPendente>().CountAsync();
    }

    public async Task RemoverPendenteAsync(int id)
    {
        var db = await ConectarAsync();
        await db.DeleteAsync<OperacaoPendente>(id);
    }

    public async Task IncrementarTentativaAsync(OperacaoPendente op)
    {
        var db = await ConectarAsync();
        op.Tentativas++;
        await db.UpdateAsync(op);
    }

    // ---- favoritos em cache (para exibir o estado sem rede) ----

    public async Task<HashSet<int>> FavoritosAsync()
    {
        var db = await ConectarAsync();
        return (await db.Table<FavoritoCache>().ToListAsync()).Select(f => f.ProdutoId).ToHashSet();
    }

    public async Task SubstituirFavoritosAsync(IEnumerable<int> ids)
    {
        var db = await ConectarAsync();
        await db.RunInTransactionAsync(c =>
        {
            c.DeleteAll<FavoritoCache>();
            c.InsertAll(ids.Select(i => new FavoritoCache { ProdutoId = i }));
        });
    }

    public async Task DefinirFavoritoAsync(int produtoId, bool favorito)
    {
        var db = await ConectarAsync();
        if (favorito) await db.InsertOrReplaceAsync(new FavoritoCache { ProdutoId = produtoId });
        else await db.DeleteAsync<FavoritoCache>(produtoId);
    }

    public async Task LimparDadosDaContaAsync()
    {
        var db = await ConectarAsync();
        await db.DeleteAllAsync<OperacaoPendente>();
        await db.DeleteAllAsync<FavoritoCache>();
    }
}
