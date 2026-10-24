using System.ComponentModel.DataAnnotations;
using loja_s.ViewModels;

namespace loja_s.Api;

// ------------------------------------------------------------------ respostas

public record PaginaDto<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int Total);

public record ProdutoDto(
    int Id,
    string Nome,
    string Sku,
    string Descricao,
    decimal Preco,
    decimal? PrecoPromocional,
    decimal PrecoEfetivo,
    int Estoque,
    string? ImagemUrl,
    string? EdicaoEspecial,
    string? Marca);

public record ItemCarrinhoDto(
    int ProdutoId,
    string Nome,
    int Quantidade,
    decimal PrecoUnitario,
    decimal Subtotal,
    string? ImagemUrl,
    int EstoqueDisponivel);

public record CarrinhoDto(
    IReadOnlyList<ItemCarrinhoDto> Itens,
    int QuantidadeTotal,
    decimal Subtotal,
    decimal Frete,
    decimal Total,
    bool CatalogoAtualizado);

public record EnderecoDto(
    int Id,
    string Nome,
    string Cep,
    string Rua,
    string Numero,
    string? Complemento,
    string Bairro,
    string Cidade,
    string Estado,
    string Pais,
    bool Principal);

public record PagamentoDto(string FormaPagamento, string Status, decimal Valor, string CodigoReferencia);

public record ItemPedidoDto(int ProdutoId, string Nome, int Quantidade, decimal PrecoUnitario, decimal Subtotal);

public record PedidoResumoDto(
    int Id,
    string NumeroPedido,
    DateTime DataPedido,
    string Status,
    string StatusPagamento,
    string FormaPagamento,
    decimal ValorTotal,
    int QuantidadeItens);

public record PedidoDetalheDto(
    int Id,
    string NumeroPedido,
    DateTime DataPedido,
    string Status,
    string StatusPagamento,
    string FormaPagamento,
    decimal Subtotal,
    decimal Frete,
    decimal Desconto,
    decimal ValorTotal,
    EnderecoDto Endereco,
    PagamentoDto? Pagamento,
    IReadOnlyList<ItemPedidoDto> Itens,
    bool PodeCancelar);

public record PedidoAdminDto(
    int Id,
    string NumeroPedido,
    DateTime DataPedido,
    string ClienteNome,
    string ClienteEmail,
    string Status,
    string StatusPagamento,
    decimal ValorTotal);

public record FavoritoDto(int ProdutoId, DateTime DataAdicionado, ProdutoDto Produto);

public record NotificacaoDto(int Id, string Categoria, string Titulo, string Mensagem, DateTime CriadaEm, bool Lida);

public record UsuarioDto(int Id, string Nome, string Sobrenome, string Email, string Tipo, string? Telefone, string Cpf);

public record LoginResponse(string AccessToken, string TipoToken, DateTime ExpiraEm, UsuarioDto Usuario);

public record ResumoContaDto(
    int QuantidadePedidos,
    int PedidosEmAberto,
    decimal TotalGasto,
    int Favoritos,
    int NotificacoesNaoLidas,
    int ItensNoCarrinho);

// ------------------------------------------------------------------ requisições

public class RegistrarRequest
{
    [Required, StringLength(80)]
    public string Nome { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Sobrenome { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Phone, StringLength(30)]
    public string? Telefone { get; set; }

    public DateTime? DataNascimento { get; set; }

    // Mesma política de senha do cadastro no site (ContaViewModels).
    [Required, StringLength(100, MinimumLength = 10)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).+$",
        ErrorMessage = "Use pelo menos 10 caracteres, com maiúscula, minúscula, número e símbolo.")]
    public string Senha { get; set; } = string.Empty;

    [Required, Compare(nameof(Senha), ErrorMessage = "A confirmação de senha não confere.")]
    public string ConfirmarSenha { get; set; } = string.Empty;

    [TrueRequired(ErrorMessage = "É necessário aceitar os termos de uso.")]
    public bool AceitouTermos { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Senha { get; set; } = string.Empty;

    /// <summary>Nome do aparelho exibido na lista de sessões da conta (opcional).</summary>
    [StringLength(250)]
    public string? Dispositivo { get; set; }
}

public class ItemCarrinhoRequest
{
    [Range(1, int.MaxValue)]
    public int ProdutoId { get; set; }

    [Range(1, 999)]
    public int Quantidade { get; set; } = 1;
}

public class QuantidadeRequest
{
    /// <summary>Zero remove o item.</summary>
    [Range(0, 999)]
    public int Quantidade { get; set; }
}

public class EnderecoRequest
{
    [Required, StringLength(100)]
    public string Nome { get; set; } = string.Empty;

    [Required, StringLength(12)]
    public string Cep { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Rua { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Numero { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Complemento { get; set; }

    [Required, StringLength(100)]
    public string Bairro { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Cidade { get; set; } = string.Empty;

    [Required, StringLength(2, MinimumLength = 2)]
    public string Estado { get; set; } = string.Empty;

    [StringLength(80)]
    public string Pais { get; set; } = "Brasil";

    public bool Principal { get; set; }
}

public class CriarPedidoRequest
{
    [Required, RegularExpression(@"^\d{3}\.?\d{3}\.?\d{3}-?\d{2}$", ErrorMessage = "CPF inválido.")]
    public string Cpf { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Telefone { get; set; } = string.Empty;

    [Required, StringLength(12)]
    public string Cep { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Rua { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Numero { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Complemento { get; set; }

    [Required, StringLength(100)]
    public string Bairro { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Cidade { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Estado { get; set; } = string.Empty;

    /// <summary>PIX, CartaoCredito, CartaoDebito ou Boleto.</summary>
    [Required]
    public string FormaPagamento { get; set; } = "PIX";

    [TrueRequired(ErrorMessage = "Você precisa confirmar os termos.")]
    public bool AceitaTermos { get; set; }
}

public class StatusPedidoRequest
{
    [Required]
    public string Status { get; set; } = string.Empty;
}

public class AjusteEstoqueRequest
{
    [Range(1, int.MaxValue)]
    public int ProdutoId { get; set; }

    /// <summary>Entrada, Saida ou Ajuste (Ajuste define o saldo final).</summary>
    [Required]
    public string Tipo { get; set; } = string.Empty;

    [Range(0, 1_000_000)]
    public int Quantidade { get; set; }

    [Required, StringLength(300)]
    public string Motivo { get; set; } = string.Empty;
}
