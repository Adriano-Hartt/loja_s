using loja_s.Data;
using loja_s.Services;
using loja_s.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace loja_s.Api;

/// <summary>Checkout e consulta de pedidos do cliente autenticado.</summary>
[Route("api/v1/pedidos")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
[Produces("application/json")]
public class PedidosApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly CarrinhoService _carrinho;
    private readonly PedidoService _pedidoService;

    public PedidosApiController(ApplicationDbContext db, CarrinhoService carrinho, PedidoService pedidoService)
    {
        _db = db;
        _carrinho = carrinho;
        _pedidoService = pedidoService;
    }

    /// <summary>Finaliza a compra com os itens do carrinho da conta (mesmo fluxo do checkout do site).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(PedidoDetalheDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Criar([FromBody] CriarPedidoRequest request)
    {
        var usuarioId = UsuarioIdAtual;

        if (await _carrinho.SincronizarComCatalogoAsync(usuarioId))
        {
            return Falha(
                StatusCodes.Status409Conflict,
                "Carrinho atualizado.",
                "O preço ou a disponibilidade de um item mudou. Revise o carrinho antes de concluir a compra.");
        }

        var itens = await _carrinho.ObterItensAsync(usuarioId);
        if (itens.Count == 0)
        {
            return Falha(StatusCodes.Status400BadRequest, "Carrinho vazio.", "Seu carrinho está vazio.");
        }

        var forma = Regras.FormasPagamento.FirstOrDefault(f =>
            f.Equals(request.FormaPagamento, StringComparison.OrdinalIgnoreCase));
        if (forma == null)
        {
            return Falha(StatusCodes.Status400BadRequest, "Forma de pagamento inválida.",
                $"Use uma das opções: {string.Join(", ", Regras.FormasPagamento)}.");
        }

        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId);
        if (usuario == null)
        {
            return Unauthorized();
        }

        try
        {
            var estado = request.Estado.Trim();
            var model = new CheckoutViewModel
            {
                NomeCompleto = usuario.Nome,
                CPF = request.Cpf.Trim(),
                Email = usuario.Email,
                Telefone = request.Telefone.Trim(),
                CEP = request.Cep.Trim(),
                Rua = request.Rua.Trim(),
                Numero = request.Numero.Trim(),
                Complemento = request.Complemento?.Trim(),
                Bairro = request.Bairro.Trim(),
                Cidade = request.Cidade.Trim(),
                Estado = estado.Length == 2 ? estado.ToUpperInvariant() : estado,
                FormaPagamento = forma,
                AceitaTermos = true,
                Desconto = 0m
            };

            usuario.CPF = model.CPF;
            usuario.Telefone = model.Telefone;
            await _db.SaveChangesAsync();

            var pedido = await _pedidoService.CriarPedidoAsync(model, itens, usuarioId);
            await _carrinho.LimparAsync(usuarioId);

            var detalhe = await CarregarDetalheAsync(pedido.Id);
            return CreatedAtAction(nameof(Obter), new { id = pedido.Id }, detalhe);
        }
        catch (InvalidOperationException ex)
        {
            return Falha(StatusCodes.Status409Conflict, "Não foi possível concluir o pedido.", ex.Message);
        }
    }

    /// <summary>Histórico de pedidos (mais recentes primeiro).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PedidoResumoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar()
    {
        var usuarioId = UsuarioIdAtual;
        var pedidos = await _db.Pedidos.AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.DataPedido)
            .Select(p => new
            {
                p.Id,
                p.NumeroPedido,
                p.DataPedido,
                p.Status,
                p.StatusPagamento,
                p.FormaPagamento,
                p.ValorTotal,
                Quantidade = p.Itens.Sum(i => i.Quantidade)
            })
            .ToListAsync();

        return Ok(pedidos.Select(p => new PedidoResumoDto(
            p.Id, p.NumeroPedido, ApiMapper.Utc(p.DataPedido), p.Status, p.StatusPagamento,
            p.FormaPagamento, p.ValorTotal, p.Quantidade)).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PedidoDetalheDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(int id)
    {
        var detalhe = await CarregarDetalheAsync(id);
        return detalhe == null
            ? Falha(StatusCodes.Status404NotFound, "Pedido não encontrado.")
            : Ok(detalhe);
    }

    /// <summary>
    /// Cancela o pedido pela stored procedure sp_CancelarPedidoCliente. Os triggers devolvem o estoque,
    /// estornam o pagamento e notificam o cliente. Requer SQL Server.
    /// </summary>
    [HttpPost("{id:int}/cancelar")]
    [ProducesResponseType(typeof(PedidoDetalheDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancelar(int id)
    {
        if (!_db.Database.IsSqlServer())
        {
            return RequerSqlServer();
        }

        try
        {
            await _db.QueryProcedureAsync("dbo.sp_CancelarPedidoCliente",
                new Dictionary<string, object?> { ["PedidoId"] = id, ["UsuarioId"] = UsuarioIdAtual });
        }
        catch (ProcedureException ex)
        {
            return FalhaDeProcedimento(ex);
        }

        return Ok(await CarregarDetalheAsync(id));
    }

    private async Task<PedidoDetalheDto?> CarregarDetalheAsync(int id)
    {
        var usuarioId = UsuarioIdAtual;
        var pedido = await _db.Pedidos.AsNoTracking()
            .Include(p => p.Itens)
            .Include(p => p.Pagamento)
            .Include(p => p.EnderecoEntrega)
            .FirstOrDefaultAsync(p => p.Id == id && p.UsuarioId == usuarioId);

        return pedido?.ParaDto();
    }
}
