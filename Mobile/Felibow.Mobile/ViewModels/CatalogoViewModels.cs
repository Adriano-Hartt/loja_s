using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Felibow.Mobile.Models;
using Felibow.Mobile.Services;

namespace Felibow.Mobile.ViewModels;

public partial class ProdutoItem : ObservableObject
{
    public ProdutoDto Dto { get; }
    public string? Imagem { get; }
    public string PrecoTexto { get; }
    public string? PrecoOriginal { get; }
    public bool TemPromocao { get; }
    public string EstoqueTexto { get; }

    [ObservableProperty] private bool _favorito;

    public ProdutoItem(ProdutoDto dto, string? imagem, bool favorito)
    {
        Dto = dto; Imagem = imagem; Favorito = favorito;
        var br = new System.Globalization.CultureInfo("pt-BR");
        PrecoTexto = dto.PrecoEfetivo.ToString("C", br);
        TemPromocao = dto.PrecoEfetivo < dto.Preco;
        PrecoOriginal = TemPromocao ? dto.Preco.ToString("C", br) : null;
        EstoqueTexto = dto.Estoque > 0 ? $"{dto.Estoque} em estoque" : "Esgotado";
    }
}

public partial class CatalogoViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly SyncService _sync;
    private readonly SessionService _session;

    public ObservableCollection<ProdutoItem> Produtos { get; } = new();
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private string _aviso = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TemAviso))] private bool _offline;
    public bool TemAviso => Offline;

    public CatalogoViewModel(ApiClient api, SyncService sync, SessionService session)
    {
        _api = api; _sync = sync; _session = session;
    }

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        var res = await _sync.CarregarCatalogoAsync(Busca);
        if (!res.Offline) await _sync.SincronizarPendentesAsync();
        var favoritos = await _sync.FavoritosAsync(online: !res.Offline);
        var pendentes = await _sync.QuantidadePendentesAsync();

        Offline = res.Offline;
        Aviso = res.Offline
            ? $"Modo offline — catálogo salvo em {res.AtualizadoEm?.ToLocalTime():dd/MM HH:mm}. " +
              $"{(pendentes > 0 ? $"{pendentes} ação(ões) aguardando envio. " : "")}Finalizar compra exige conexão."
            : "";
        Produtos.Clear();
        foreach (var p in res.Produtos)
            Produtos.Add(new ProdutoItem(p, _api.Midia(p.ImagemUrl), favoritos.Contains(p.Id)));
    });

    [RelayCommand]
    private Task Abrir(ProdutoItem item) => Shell.Current.GoToAsync($"produto?id={item.Dto.Id}");

    [RelayCommand]
    private Task AlternarFavorito(ProdutoItem item) => ExecutarAsync(async () =>
    {
        var novo = !item.Favorito;
        var enviado = await _sync.DefinirFavoritoAsync(item.Dto.Id, novo);
        item.Favorito = novo;
        if (!enviado)
        {
            Offline = true;
            Aviso = "Sem conexão: sua ação foi salva e será enviada quando a internet voltar.";
        }
    });
}

public partial class ProdutoDetalheViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ApiClient _api;
    private readonly SyncService _sync;

    [ObservableProperty] private ProdutoItem? _produto;
    [ObservableProperty] private int _quantidade = 1;
    [ObservableProperty] private string _mensagem = "";

    public ProdutoDetalheViewModel(ApiClient api, SyncService sync) { _api = api; _sync = sync; }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var v) || !int.TryParse(v?.ToString(), out var id)) return;
        await ExecutarAsync(async () =>
        {
            var dto = await _sync.ProdutoAsync(id) ?? throw new ApiException("Produto não encontrado.");
            Produto = new ProdutoItem(dto, _api.Midia(dto.ImagemUrl), false);
        });
    }

    [RelayCommand] private void Mais() { if (Produto != null && Quantidade < Math.Min(99, Produto.Dto.Estoque)) Quantidade++; }
    [RelayCommand] private void Menos() { if (Quantidade > 1) Quantidade--; }

    [RelayCommand]
    private Task Adicionar() => ExecutarAsync(async () =>
    {
        if (Produto == null) return;
        var enviado = await _sync.AdicionarAoCarrinhoAsync(Produto.Dto.Id, Quantidade);
        Mensagem = enviado
            ? "Adicionado ao carrinho."
            : "Sem conexão: item salvo e será enviado ao carrinho quando a internet voltar.";
    });
}
