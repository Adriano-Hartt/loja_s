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
    public bool TemAviso => false;

    [ObservableProperty] private string _temaTexto = Application.Current?.RequestedTheme == AppTheme.Dark ? "\u2600\uFE0F" : "\U0001F319";

    [RelayCommand]
    private void AlternarTema()
    {
        var app = Application.Current;
        if (app == null) return;
        var novo = app.RequestedTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        app.UserAppTheme = novo;
        Preferences.Default.Set("tema", novo == AppTheme.Dark ? "dark" : "light");
        TemaTexto = novo == AppTheme.Dark ? "\u2600\uFE0F" : "\U0001F319";
    }

    public CatalogoViewModel(ApiClient api, SyncService sync, SessionService session)
    {
        _api = api; _sync = sync; _session = session;
    }

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        await _session.CarregarAsync();
        var res = await _sync.CarregarCatalogoAsync(Busca);
        if (!res.Offline && _session.Autenticado) await _sync.SincronizarPendentesAsync();
        var favoritos = _session.Autenticado ? await _sync.FavoritosAsync(online: !res.Offline) : new HashSet<int>();
        var pendentes = await _sync.QuantidadePendentesAsync();

        Offline = res.Offline;
        Aviso = res.Offline
            ? $"Modo offline — {(res.AtualizadoEm.HasValue ? $"cat\u00e1logo salvo em {res.AtualizadoEm.Value.ToLocalTime():dd/MM HH:mm}. " : "mostrando cat\u00e1logo de demonstra\u00e7\u00e3o. ")}" +
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

    private readonly SessionService _session;
    public ProdutoDetalheViewModel(ApiClient api, SyncService sync, SessionService session) { _api = api; _sync = sync; _session = session; }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var v) || !int.TryParse(v?.ToString(), out var id)) return;
        await ExecutarAsync(async () =>
        {
            var dto = await _sync.ProdutoAsync(id) ?? throw new ApiException("Produto não encontrado.");
            var favorito = false;
            if (_session.Autenticado)
            {
                try { favorito = (await _sync.FavoritosAsync(online: true)).Contains(dto.Id); } catch { }
            }
            Produto = new ProdutoItem(dto, _api.Midia(dto.ImagemUrl), favorito);
        });
    }

    [RelayCommand]
    private async Task AlternarFavorito()
    {
        if (Produto == null) return;
        if (!_session.Autenticado) { await Shell.Current.GoToAsync("//login"); return; }
        await ExecutarAsync(async () =>
        {
            var novo = !Produto.Favorito;
            var enviado = await _sync.DefinirFavoritoAsync(Produto.Dto.Id, novo);
            Produto.Favorito = novo;
            Mensagem = enviado
                ? (novo ? "Adicionado aos favoritos." : "Removido dos favoritos.")
                : "Sem conexão: sua ação foi salva e será enviada quando a internet voltar.";
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
