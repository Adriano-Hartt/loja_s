using System.Security.Claims;
using System.Text;
using loja_s.Data;
using loja_s.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace loja_s.Api;

/// <summary>Regras de negócio compartilhadas com o site MVC (PedidoService / CheckoutController).</summary>
public static class Regras
{
    /// <summary>Mesmo valor aplicado em PedidoService.CriarPedidoAsync quando há itens.</summary>
    public const decimal FreteFixo = 19.90m;

    public static readonly string[] FormasPagamento = { "PIX", "CartaoCredito", "CartaoDebito", "Boleto" };

    public static readonly string[] StatusCancelaveis = { "Pendente", "AguardandoPagamento", "Finalizado" };

    public const string EsquemaJwt = "Bearer";
}

/// <summary>Base dos controllers da API: validação automática ([ApiController]) e utilidades comuns.</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected int UsuarioIdAtual =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    protected ObjectResult Falha(int status, string titulo, string? detalhe = null) =>
        Problem(statusCode: status, title: titulo, detail: detalhe);

    /// <summary>Recursos que dependem de triggers/procedures só existem com SQL Server.</summary>
    protected ObjectResult RequerSqlServer() =>
        Falha(
            StatusCodes.Status501NotImplemented,
            "Recurso indisponível neste banco de dados.",
            "Este recurso usa triggers e stored procedures e exige SQL Server (Database__Provider=SqlServer).");

    protected ObjectResult FalhaDeProcedimento(ProcedureException ex) => ex.Numero switch
    {
        50001 => Falha(StatusCodes.Status404NotFound, "Registro não encontrado.", ex.Message),
        50002 => Falha(StatusCodes.Status409Conflict, "Operação não permitida no estado atual.", ex.Message),
        50003 => Falha(StatusCodes.Status403Forbidden, "Acesso negado.", ex.Message),
        _ => Falha(StatusCodes.Status400BadRequest, "Requisição inválida.", ex.Message)
    };

    protected string? Midia(string? caminho) => MediaUrl.Resolve(Request, caminho);
}

public static class MediaUrl
{
    /// <summary>
    /// Converte caminhos como "~/img/produtos/x.png" (guardados no banco) em URL absoluta
    /// no mesmo host que atendeu a requisição, para o app mobile conseguir baixar a imagem.
    /// </summary>
    public static string? Resolve(HttpRequest request, string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            return null;
        }

        var valor = caminho.Trim();
        if (valor.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            valor.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return valor;
        }

        valor = valor.TrimStart('~');
        if (!valor.StartsWith('/'))
        {
            valor = "/" + valor;
        }

        var segmentos = valor.Split('/').Select(Uri.EscapeDataString);
        return $"{request.Scheme}://{request.Host}{string.Join('/', segmentos)}";
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "felibow-loja";
    public string Audience { get; set; } = "felibow-clientes";
    public string Key { get; set; } = string.Empty;
    public int ExpiracaoHoras { get; set; } = 8;
}

public interface IJwtTokenService
{
    (string Token, DateTime ExpiraEmUtc) Criar(Usuario usuario, string chaveSessao, string securityStamp);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
    }

    public (string Token, DateTime ExpiraEmUtc) Criar(Usuario usuario, string chaveSessao, string securityStamp)
    {
        var expira = DateTime.UtcNow.AddHours(_options.ExpiracaoHoras);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.Nome),
                new Claim(ClaimTypes.Email, usuario.Email),
                new Claim(ClaimTypes.Role, usuario.Tipo),
                new Claim("AccountSecurityStamp", securityStamp),
                new Claim("AccountSessionId", chaveSessao)
            }),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = expira,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expira);
    }
}

public static class ApiMapper
{
    /// <summary>Datas vêm do banco sem "Kind"; a API sempre as publica como UTC.</summary>
    public static DateTime Utc(DateTime valor) => DateTime.SpecifyKind(valor, DateTimeKind.Utc);

    public static DateTime? Utc(DateTime? valor) => valor.HasValue ? Utc(valor.Value) : null;

    public static ProdutoDto ParaDto(this Produto p, HttpRequest request) => new(
        p.Id,
        p.Nome,
        p.SKU,
        p.Descricao,
        p.Preco,
        p.PrecoPromocional,
        p.PrecoEfetivo,
        p.Estoque,
        MediaUrl.Resolve(request, p.ImagemUrl),
        p.EdicaoEspecial,
        string.IsNullOrWhiteSpace(p.Marca) ? null : p.Marca);

    public static EnderecoDto ParaDto(this Endereco e) => new(
        e.Id, e.Nome, e.CEP, e.Rua, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Estado, e.Pais, e.Principal);

    public static UsuarioDto ParaDto(this Usuario u) => new(
        u.Id,
        u.Nome,
        u.PerfilConta?.Sobrenome ?? string.Empty,
        u.Email,
        u.Tipo,
        u.Telefone,
        u.CPF);

    public static PedidoDetalheDto ParaDto(this Pedido p) => new(
        p.Id,
        p.NumeroPedido,
        Utc(p.DataPedido),
        p.Status,
        p.StatusPagamento,
        p.FormaPagamento,
        p.Subtotal,
        p.Frete,
        p.Desconto,
        p.ValorTotal,
        p.EnderecoEntrega!.ParaDto(),
        p.Pagamento == null
            ? null
            : new PagamentoDto(p.Pagamento.FormaPagamento, p.Pagamento.Status, p.Pagamento.Valor, p.Pagamento.CodigoReferencia),
        p.Itens
            .OrderBy(i => i.Id)
            .Select(i => new ItemPedidoDto(i.ProdutoId, i.NomeProduto, i.Quantidade, i.PrecoUnitario, i.Subtotal))
            .ToList(),
        Regras.StatusCancelaveis.Contains(p.Status));

    public static int Inteiro(this IReadOnlyDictionary<string, object?> linha, string coluna) =>
        linha.TryGetValue(coluna, out var valor) && valor != null ? Convert.ToInt32(valor) : 0;

    public static decimal Decimal(this IReadOnlyDictionary<string, object?> linha, string coluna) =>
        linha.TryGetValue(coluna, out var valor) && valor != null ? Convert.ToDecimal(valor) : 0m;
}
