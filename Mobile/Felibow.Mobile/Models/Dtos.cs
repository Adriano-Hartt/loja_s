namespace Felibow.Mobile.Models;

// Espelham os DTOs de loja_s/Api/ApiDtos.cs (JSON camelCase).
public record PaginaDto<T>(List<T> Itens, int Pagina, int TamanhoPagina, int Total);
public record ProdutoDto(int Id, string Nome, string Sku, string Descricao, decimal Preco, decimal? PrecoPromocional,
    decimal PrecoEfetivo, int Estoque, string? ImagemUrl, string? EdicaoEspecial, string? Marca);
public record ItemCarrinhoDto(int ProdutoId, string Nome, int Quantidade, decimal PrecoUnitario, decimal Subtotal,
    string? ImagemUrl, int EstoqueDisponivel);
public record CarrinhoDto(List<ItemCarrinhoDto> Itens, int QuantidadeTotal, decimal Subtotal, decimal Frete,
    decimal Total, bool CatalogoAtualizado);
public record EnderecoDto(int Id, string Nome, string Cep, string Rua, string Numero, string? Complemento,
    string Bairro, string Cidade, string Estado, string Pais, bool Principal);
public record PagamentoDto(string FormaPagamento, string Status, decimal Valor, string CodigoReferencia);
public record ItemPedidoDto(int ProdutoId, string Nome, int Quantidade, decimal PrecoUnitario, decimal Subtotal);
public record PedidoResumoDto(int Id, string NumeroPedido, DateTime DataPedido, string Status, string StatusPagamento,
    string FormaPagamento, decimal ValorTotal, int QuantidadeItens);
public record PedidoDetalheDto(int Id, string NumeroPedido, DateTime DataPedido, string Status, string StatusPagamento,
    string FormaPagamento, decimal Subtotal, decimal Frete, decimal Desconto, decimal ValorTotal, EnderecoDto Endereco,
    PagamentoDto? Pagamento, List<ItemPedidoDto> Itens, bool PodeCancelar);
public record FavoritoDto(int ProdutoId, DateTime DataAdicionado, ProdutoDto Produto);
public record NotificacaoDto(int Id, string Categoria, string Titulo, string Mensagem, DateTime CriadaEm, bool Lida);
public record UsuarioDto(int Id, string Nome, string Sobrenome, string Email, string Tipo, string? Telefone, string Cpf);
public record LoginResponse(string AccessToken, string TipoToken, DateTime ExpiraEm, UsuarioDto Usuario);
public record ResumoContaDto(int QuantidadePedidos, int PedidosEmAberto, decimal TotalGasto, int Favoritos,
    int NotificacoesNaoLidas, int ItensNoCarrinho);

public record LoginRequest(string Email, string Senha, string? Dispositivo);
public record RegistrarRequest(string Nome, string Sobrenome, string Email, string? Telefone, string Senha,
    string ConfirmarSenha, bool AceitouTermos);
public record ItemCarrinhoRequest(int ProdutoId, int Quantidade);
public record QuantidadeRequest(int Quantidade);
public record CriarPedidoRequest(string Cpf, string Telefone, string Cep, string Rua, string Numero, string? Complemento,
    string Bairro, string Cidade, string Estado, string FormaPagamento, bool AceitaTermos);
