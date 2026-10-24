using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Felibow.Mobile.Models;
using Felibow.Mobile.Services;

namespace Felibow.Mobile.ViewModels;

public partial class ItemCarrinhoItem : ObservableObject
{
    public ItemCarrinhoDto Dto { get; }
    public string? Imagem { get; }
    public string PrecoTexto { get; }
    public string SubtotalTexto { get; }
    public ItemCarrinhoItem(ItemCarrinhoDto dto, string? imagem)
    {
        Dto = dto; Imagem = imagem;
        var br = new System.Globalization.CultureInfo("pt-BR");
        PrecoTexto = dto.PrecoUnitario.ToString("C", br);
        SubtotalTexto = dto.Subtotal.ToString("C", br);
    }
}

public partial class CarrinhoViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    public ObservableCollection<ItemCarrinhoItem> Itens { get; } = new();
    [ObservableProperty] private string _subtotal = "", _frete = "", _total = "", _aviso = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Vazio))] private bool _temItens;
    public bool Vazio => !TemItens;

    public CarrinhoViewModel(ApiClient api) => _api = api;

    private void Aplicar(CarrinhoDto c)
    {
        Itens.Clear();
        foreach (var i in c.Itens) Itens.Add(new ItemCarrinhoItem(i, _api.Midia(i.ImagemUrl)));
        TemItens = c.Itens.Count > 0;
        Subtotal = Moeda(c.Subtotal); Frete = Moeda(c.Frete); Total = Moeda(c.Total);
        Aviso = c.CatalogoAtualizado ? "Alguns itens mudaram de preço ou estoque e foram ajustados." : "";
    }

    [RelayCommand] public Task Carregar() => ExecutarAsync(async () => Aplicar(await _api.CarrinhoAsync()));

    [RelayCommand]
    private Task Mais(ItemCarrinhoItem i) =>
        ExecutarAsync(async () => Aplicar(await _api.AtualizarItemAsync(i.Dto.ProdutoId, i.Dto.Quantidade + 1)));

    [RelayCommand]
    private Task Menos(ItemCarrinhoItem i) =>
        ExecutarAsync(async () => Aplicar(await _api.AtualizarItemAsync(i.Dto.ProdutoId, i.Dto.Quantidade - 1)));

    [RelayCommand]
    private Task Remover(ItemCarrinhoItem i) =>
        ExecutarAsync(async () => Aplicar(await _api.RemoverItemAsync(i.Dto.ProdutoId)));

    [RelayCommand] private Task Finalizar() => Shell.Current.GoToAsync("checkout");
}

public partial class CheckoutViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    public string[] Formas { get; } = { "PIX", "CartaoCredito", "CartaoDebito", "Boleto" };

    [ObservableProperty] private string _cpf = "", _telefone = "", _cep = "", _rua = "", _numero = "",
        _complemento = "", _bairro = "", _cidade = "", _estado = "", _total = "";
    [ObservableProperty] private string _forma = "PIX";
    [ObservableProperty] private bool _aceitaTermos;

    public CheckoutViewModel(ApiClient api) => _api = api;

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        var eu = await _api.EuAsync();
        Cpf = eu.Cpf; Telefone = eu.Telefone ?? "";
        var end = (await _api.EnderecosAsync()).FirstOrDefault(e => e.Principal) ?? (await _api.EnderecosAsync()).FirstOrDefault();
        if (end != null)
        {
            Cep = end.Cep; Rua = end.Rua; Numero = end.Numero; Complemento = end.Complemento ?? "";
            Bairro = end.Bairro; Cidade = end.Cidade; Estado = end.Estado;
        }
        Total = Moeda((await _api.CarrinhoAsync()).Total);
    });

    [RelayCommand]
    private Task Confirmar() => ExecutarAsync(async () =>
    {
        var pedido = await _api.CriarPedidoAsync(new CriarPedidoRequest(Cpf, Telefone, Cep, Rua, Numero,
            string.IsNullOrWhiteSpace(Complemento) ? null : Complemento, Bairro, Cidade, Estado, Forma, AceitaTermos));
        await Shell.Current.GoToAsync($"//main/pedidos");
        await Shell.Current.GoToAsync($"pedido?id={pedido.Id}");
    });
}

public partial class PedidosViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    public ObservableCollection<PedidoResumoDto> Pedidos { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Vazio))] private bool _temPedidos;
    public bool Vazio => !TemPedidos;

    public PedidosViewModel(ApiClient api) => _api = api;

    [RelayCommand]
    public Task Carregar() => ExecutarAsync(async () =>
    {
        var lista = await _api.PedidosAsync();
        Pedidos.Clear();
        foreach (var p in lista) Pedidos.Add(p);
        TemPedidos = lista.Count > 0;
    });

    [RelayCommand] private Task Abrir(PedidoResumoDto p) => Shell.Current.GoToAsync($"pedido?id={p.Id}");
}

public partial class PedidoDetalheViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ApiClient _api;
    [ObservableProperty] private PedidoDetalheDto? _pedido;
    [ObservableProperty] private string _totalTexto = "", _enderecoTexto = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TemCodigo))] private string _codigoPagamento = "";
    public bool TemCodigo => !string.IsNullOrEmpty(CodigoPagamento);
    public ObservableCollection<string> Linhas { get; } = new();

    public PedidoDetalheViewModel(ApiClient api) => _api = api;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && int.TryParse(v?.ToString(), out var id))
            await ExecutarAsync(async () => Aplicar(await _api.PedidoAsync(id)));
    }

    private void Aplicar(PedidoDetalheDto p)
    {
        Pedido = p;
        TotalTexto = Moeda(p.ValorTotal);
        EnderecoTexto = $"{p.Endereco.Rua}, {p.Endereco.Numero} - {p.Endereco.Bairro}, {p.Endereco.Cidade}/{p.Endereco.Estado}";
        CodigoPagamento = p.Pagamento?.CodigoReferencia ?? "";
        Linhas.Clear();
        foreach (var i in p.Itens) Linhas.Add($"{i.Quantidade}x {i.Nome} — {Moeda(i.Subtotal)}");
    }

    [RelayCommand]
    private async Task Copiar()
    {
        await Clipboard.Default.SetTextAsync(CodigoPagamento);
        await Shell.Current.DisplayAlert("Copiado", "Código de pagamento copiado.", "OK");
    }

    [RelayCommand]
    private Task Cancelar() => ExecutarAsync(async () =>
    {
        if (Pedido == null) return;
        if (!await Shell.Current.DisplayAlert("Cancelar pedido", "Deseja realmente cancelar?", "Cancelar pedido", "Voltar")) return;
        Aplicar(await _api.CancelarPedidoAsync(Pedido.Id));
    });
}
