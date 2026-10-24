using Felibow.Mobile.Models;

namespace Felibow.Mobile.Services;

public record CatalogoResultado(List<ProdutoDto> Produtos, bool Offline, DateTime? AtualizadoEm);

/// <summary>Sincronização do catálogo: online grava no SQLite local; offline lê do cache.</summary>
public class SyncService
{
    private readonly ApiClient _api;
    private readonly LocalDatabase _local;

    public SyncService(ApiClient api, LocalDatabase local)
    {
        _api = api;
        _local = local;
    }

    public async Task<CatalogoResultado> CarregarCatalogoAsync(string? busca)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(busca))
            {
                var todos = new List<ProdutoDto>();
                for (var pagina = 1; ; pagina++)
                {
                    var p = await _api.ProdutosAsync(null, pagina, 50);
                    todos.AddRange(p.Itens);
                    if (todos.Count >= p.Total || p.Itens.Count == 0) break;
                }
                await _local.SubstituirCatalogoAsync(todos);
                return new CatalogoResultado(todos, false, DateTime.UtcNow);
            }

            var filtrados = await _api.ProdutosAsync(busca);
            return new CatalogoResultado(filtrados.Itens, false, DateTime.UtcNow);
        }
        catch (ApiException ex) when (ex.SemConexao)
        {
            return new CatalogoResultado(await _local.CatalogoAsync(busca), true, await _local.UltimaSincronizacaoAsync());
        }
    }

    public async Task<ProdutoDto?> ProdutoAsync(int id)
    {
        try { return await _api.ProdutoAsync(id); }
        catch (ApiException ex) when (ex.SemConexao) { return await _local.ProdutoAsync(id); }
    }

    // ---------------------------------------------------------------- operações offline

    public event Action<int>? PendentesAlterados;

    public Task<int> QuantidadePendentesAsync() => _local.ContarPendentesAsync();

    /// <summary>Favorita/desfavorita: envia agora ou, sem rede, guarda na fila e atualiza o cache local.</summary>
    public async Task<bool> DefinirFavoritoAsync(int produtoId, bool favorito)
    {
        await _local.DefinirFavoritoAsync(produtoId, favorito);
        try
        {
            if (favorito) await _api.FavoritarAsync(produtoId);
            else await _api.DesfavoritarAsync(produtoId);
            return true;
        }
        catch (ApiException ex) when (ex.SemConexao)
        {
            await Enfileirar(favorito ? TipoOperacao.Favoritar : TipoOperacao.Desfavoritar, produtoId, 0);
            return false;
        }
    }

    /// <summary>Adiciona ao carrinho; sem rede, a ação fica pendente. Retorna true se foi enviada agora.</summary>
    public async Task<bool> AdicionarAoCarrinhoAsync(int produtoId, int quantidade)
    {
        try
        {
            await _api.AdicionarAoCarrinhoAsync(produtoId, quantidade);
            return true;
        }
        catch (ApiException ex) when (ex.SemConexao)
        {
            await Enfileirar(TipoOperacao.AdicionarAoCarrinho, produtoId, quantidade);
            return false;
        }
    }

    public async Task<HashSet<int>> FavoritosAsync(bool online)
    {
        if (online)
        {
            try
            {
                var ids = (await _api.FavoritosAsync()).Select(f => f.ProdutoId).ToHashSet();
                // Operações ainda pendentes valem mais do que o servidor.
                foreach (var op in await _local.PendentesAsync())
                {
                    if (op.Tipo == TipoOperacao.Favoritar) ids.Add(op.ProdutoId);
                    if (op.Tipo == TipoOperacao.Desfavoritar) ids.Remove(op.ProdutoId);
                }
                await _local.SubstituirFavoritosAsync(ids);
                return ids;
            }
            catch (ApiException ex) when (ex.SemConexao) { }
        }
        return await _local.FavoritosAsync();
    }

    private static readonly SemaphoreSlim Trava = new(1, 1);

    /// <summary>Reenvia a fila em ordem. Para ao primeiro erro de rede; descarta ações rejeitadas pelo servidor (ex.: sem estoque).</summary>
    public async Task<(int Enviadas, int Descartadas)> SincronizarPendentesAsync()
    {
        if (!await Trava.WaitAsync(0)) return (0, 0);
        int enviadas = 0, descartadas = 0;
        try
        {
            foreach (var op in await _local.PendentesAsync())
            {
                try
                {
                    switch (op.Tipo)
                    {
                        case TipoOperacao.Favoritar: await _api.FavoritarAsync(op.ProdutoId); break;
                        case TipoOperacao.Desfavoritar: await _api.DesfavoritarAsync(op.ProdutoId); break;
                        case TipoOperacao.AdicionarAoCarrinho:
                            await _api.AdicionarAoCarrinhoAsync(op.ProdutoId, op.Quantidade); break;
                    }
                    await _local.RemoverPendenteAsync(op.Id);
                    enviadas++;
                }
                catch (ApiException ex) when (ex.SemConexao)
                {
                    break; // continua offline; tenta de novo depois
                }
                catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.Unauthorized)
                {
                    break; // sessão expirada: mantém a fila para depois do login
                }
                catch (ApiException)
                {
                    await _local.RemoverPendenteAsync(op.Id); // recusada pelo servidor
                    descartadas++;
                }
            }
        }
        finally
        {
            Trava.Release();
            PendentesAlterados?.Invoke(await _local.ContarPendentesAsync());
        }
        return (enviadas, descartadas);
    }

    public Task LimparDadosDaContaAsync() => _local.LimparDadosDaContaAsync();

    private async Task Enfileirar(TipoOperacao tipo, int produtoId, int quantidade)
    {
        await _local.EnfileirarAsync(new OperacaoPendente { Tipo = tipo, ProdutoId = produtoId, Quantidade = quantidade });
        PendentesAlterados?.Invoke(await _local.ContarPendentesAsync());
    }
}
