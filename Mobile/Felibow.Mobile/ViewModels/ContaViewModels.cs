using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Felibow.Mobile.Models;
using Felibow.Mobile.Services;

namespace Felibow.Mobile.ViewModels;

public partial class ContaViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly SessionService _session;
    private readonly SettingsService _settings;
    private readonly SyncService _sync;

    [ObservableProperty] private string _nome = "", _email = "", _resumo = "", _servidor = "";

    public ContaViewModel(ApiClient api, SessionService session, SettingsService settings, SyncService sync)
    {
        _api = api; _session = session; _settings = settings; _sync = sync;
        Servidor = settings.BaseUrl;
    }

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        var eu = await _api.EuAsync();
        Nome = $"{eu.Nome} {eu.Sobrenome}"; Email = eu.Email;
        var r = await _api.ResumoAsync();
        Resumo = $"{r.QuantidadePedidos} pedidos ({r.PedidosEmAberto} em andamento) · {Moeda(r.TotalGasto)} gastos\n" +
                 $"{r.Favoritos} favoritos · {r.NotificacoesNaoLidas} notificações novas";
    });

    [RelayCommand] private void SalvarServidor() { _settings.BaseUrl = Servidor; Erro = ""; }
    [RelayCommand] private Task Favoritos() => Shell.Current.GoToAsync("favoritos");
    [RelayCommand] private Task Notificacoes() => Shell.Current.GoToAsync("notificacoes");

    [RelayCommand]
    private async Task Sair()
    {
        try { await _api.LogoutAsync(); } catch (ApiException) { }
        _session.Limpar();
        await _sync.LimparDadosDaContaAsync();
        await Shell.Current.GoToAsync("//login");
    }
}

public partial class FavoritosViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    public ObservableCollection<ProdutoItem> Itens { get; } = new();
    public FavoritosViewModel(ApiClient api) => _api = api;

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        Itens.Clear();
        foreach (var f in await _api.FavoritosAsync())
            Itens.Add(new ProdutoItem(f.Produto, _api.Midia(f.Produto.ImagemUrl), true));
    });

    [RelayCommand] private Task Abrir(ProdutoItem i) => Shell.Current.GoToAsync($"produto?id={i.Dto.Id}");

    [RelayCommand]
    private Task Remover(ProdutoItem i) => ExecutarAsync(async () =>
    {
        await _api.DesfavoritarAsync(i.Dto.Id);
        Itens.Remove(i);
    });
}

public partial class NotificacoesViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    public ObservableCollection<NotificacaoDto> Itens { get; } = new();
    public NotificacoesViewModel(ApiClient api) => _api = api;

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        var lista = await _api.NotificacoesAsync();
        Itens.Clear();
        foreach (var n in lista) Itens.Add(n);
        if (lista.Any(n => !n.Lida)) await _api.MarcarNotificacoesLidasAsync();
    });
}
