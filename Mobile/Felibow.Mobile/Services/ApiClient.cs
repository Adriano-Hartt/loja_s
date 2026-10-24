using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Felibow.Mobile.Models;

namespace Felibow.Mobile.Services;

public class ApiException : Exception
{
    public HttpStatusCode Status { get; }
    public bool SemConexao { get; }
    public ApiException(string mensagem, HttpStatusCode status = 0, bool semConexao = false) : base(mensagem)
    {
        Status = status; SemConexao = semConexao;
    }
}

/// <summary>Cliente HTTP da API REST (Bearer JWT, erros ProblemDetails, 401 => encerra sessão).</summary>
public class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SettingsService _settings;
    private readonly SessionService _session;

    public event Action? SessaoExpirada;

    public ApiClient(SettingsService settings, SessionService session)
    {
        _settings = settings;
        _session = session;
    }

    public Task<LoginResponse> LoginAsync(string email, string senha) =>
        SendAsync<LoginResponse>(HttpMethod.Post, "api/v1/auth/login",
            new LoginRequest(email, senha, $"{DeviceInfo.Manufacturer} {DeviceInfo.Model}"), autenticado: false);

    public Task<UsuarioDto> RegistrarAsync(RegistrarRequest r) =>
        SendAsync<UsuarioDto>(HttpMethod.Post, "api/v1/auth/registrar", r, autenticado: false);

    public Task LogoutAsync() => SendAsync(HttpMethod.Post, "api/v1/auth/logout");
    public Task<UsuarioDto> EuAsync() => SendAsync<UsuarioDto>(HttpMethod.Get, "api/v1/auth/eu");

    public Task<PaginaDto<ProdutoDto>> ProdutosAsync(string? busca, int pagina = 1, int tamanho = 50) =>
        SendAsync<PaginaDto<ProdutoDto>>(HttpMethod.Get,
            $"api/v1/produtos?pagina={pagina}&tamanho={tamanho}&busca={Uri.EscapeDataString(busca ?? "")}", autenticado: false);

    public Task<ProdutoDto> ProdutoAsync(int id) =>
        SendAsync<ProdutoDto>(HttpMethod.Get, $"api/v1/produtos/{id}", autenticado: false);

    public Task<CarrinhoDto> CarrinhoAsync() => SendAsync<CarrinhoDto>(HttpMethod.Get, "api/v1/carrinho");
    public Task<CarrinhoDto> AdicionarAoCarrinhoAsync(int produtoId, int qtd) =>
        SendAsync<CarrinhoDto>(HttpMethod.Post, "api/v1/carrinho/itens", new ItemCarrinhoRequest(produtoId, qtd));
    public Task<CarrinhoDto> AtualizarItemAsync(int produtoId, int qtd) =>
        SendAsync<CarrinhoDto>(HttpMethod.Put, $"api/v1/carrinho/itens/{produtoId}", new QuantidadeRequest(qtd));
    public Task<CarrinhoDto> RemoverItemAsync(int produtoId) =>
        SendAsync<CarrinhoDto>(HttpMethod.Delete, $"api/v1/carrinho/itens/{produtoId}");

    public Task<List<FavoritoDto>> FavoritosAsync() => SendAsync<List<FavoritoDto>>(HttpMethod.Get, "api/v1/favoritos");
    public Task FavoritarAsync(int produtoId) => SendAsync(HttpMethod.Put, $"api/v1/favoritos/{produtoId}");
    public Task DesfavoritarAsync(int produtoId) => SendAsync(HttpMethod.Delete, $"api/v1/favoritos/{produtoId}");

    public Task<List<EnderecoDto>> EnderecosAsync() => SendAsync<List<EnderecoDto>>(HttpMethod.Get, "api/v1/enderecos");

    public Task<PedidoDetalheDto> CriarPedidoAsync(CriarPedidoRequest r) =>
        SendAsync<PedidoDetalheDto>(HttpMethod.Post, "api/v1/pedidos", r);
    public Task<List<PedidoResumoDto>> PedidosAsync() => SendAsync<List<PedidoResumoDto>>(HttpMethod.Get, "api/v1/pedidos");
    public Task<PedidoDetalheDto> PedidoAsync(int id) => SendAsync<PedidoDetalheDto>(HttpMethod.Get, $"api/v1/pedidos/{id}");
    public Task<PedidoDetalheDto> CancelarPedidoAsync(int id) =>
        SendAsync<PedidoDetalheDto>(HttpMethod.Post, $"api/v1/pedidos/{id}/cancelar");

    public Task<List<NotificacaoDto>> NotificacoesAsync() => SendAsync<List<NotificacaoDto>>(HttpMethod.Get, "api/v1/notificacoes");
    public Task MarcarNotificacoesLidasAsync() => SendAsync(HttpMethod.Post, "api/v1/notificacoes/lidas");
    public Task<ResumoContaDto> ResumoAsync() => SendAsync<ResumoContaDto>(HttpMethod.Get, "api/v1/conta/resumo");

    /// <summary>Converte caminho relativo de imagem (/uploads/..) em URL absoluta.</summary>
    public string? Midia(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        return caminho.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? caminho
            : _settings.BaseUrl + (caminho.StartsWith('/') ? caminho : "/" + caminho);
    }

    private async Task SendAsync(HttpMethod metodo, string rota, object? corpo = null, bool autenticado = true) =>
        await ExecutarAsync(metodo, rota, corpo, autenticado);

    private async Task<T> SendAsync<T>(HttpMethod metodo, string rota, object? corpo = null, bool autenticado = true)
    {
        using var resposta = await ExecutarAsync(metodo, rota, corpo, autenticado);
        var dado = await resposta.Content.ReadFromJsonAsync<T>(Json);
        return dado ?? throw new ApiException("Resposta vazia do servidor.", resposta.StatusCode);
    }

    private async Task<HttpResponseMessage> ExecutarAsync(HttpMethod metodo, string rota, object? corpo, bool autenticado)
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            throw new ApiException("Informe o endereço do servidor na aba Conta.");

        using var req = new HttpRequestMessage(metodo, $"{_settings.BaseUrl}/{rota}");
        if (corpo != null) req.Content = JsonContent.Create(corpo, options: Json);
        if (autenticado && _session.Token != null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);

        HttpResponseMessage resposta;
        try
        {
            resposta = await _http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException("Sem conexão com o servidor. Verifique a rede e o endereço configurado.", 0, true);
        }

        if (resposta.IsSuccessStatusCode) return resposta;

        var msg = await LerErroAsync(resposta);
        var status = resposta.StatusCode;
        resposta.Dispose();

        if (status == HttpStatusCode.Unauthorized && autenticado)
        {
            _session.Limpar();
            SessaoExpirada?.Invoke();
            throw new ApiException("Sua sessão expirou. Entre novamente.", status);
        }

        throw new ApiException(msg, status);
    }

    private static async Task<string> LerErroAsync(HttpResponseMessage r)
    {
        try
        {
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Object)
            {
                var textos = erros.EnumerateObject()
                    .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString()))
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                var junto = string.Join("\n", textos);
                if (junto.Length > 0) return junto;
            }
            if (raiz.TryGetProperty("detail", out var d) && !string.IsNullOrWhiteSpace(d.GetString())) return d.GetString()!;
            if (raiz.TryGetProperty("title", out var t) && !string.IsNullOrWhiteSpace(t.GetString())) return t.GetString()!;
        }
        catch { /* corpo não-JSON */ }
        return r.StatusCode == HttpStatusCode.TooManyRequests
            ? "Muitas tentativas. Aguarde um instante."
            : $"Erro {(int)r.StatusCode} no servidor.";
    }
}
