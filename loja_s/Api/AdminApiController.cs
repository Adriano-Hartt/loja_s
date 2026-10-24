using loja_s.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace loja_s.Api;

/// <summary>Estrutura administrativa: pedidos, estoque e relatórios (apenas perfil Admin).</summary>
[Route("api/v1/admin")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt, Roles = "Admin")]
[Produces("application/json")]
public class AdminApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public AdminApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("pedidos")]
    [ProducesResponseType(typeof(IReadOnlyList<PedidoAdminDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pedidos([FromQuery] string? status, [FromQuery] int tamanho = 50)
    {
        tamanho = Math.Clamp(tamanho, 1, 200);
        var consulta = _db.Pedidos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            consulta = consulta.Where(p => p.Status == status);
        }

        var pedidos = await consulta
            .OrderByDescending(p => p.DataPedido)
            .Take(tamanho)
            .Select(p => new
            {
                p.Id, p.NumeroPedido, p.DataPedido, p.Status, p.StatusPagamento, p.ValorTotal,
                Nome = p.Usuario!.Nome, p.Usuario.Email
            })
            .ToListAsync();

        return Ok(pedidos.Select(p => new PedidoAdminDto(
            p.Id, p.NumeroPedido, ApiMapper.Utc(p.DataPedido), p.Nome, p.Email,
            p.Status, p.StatusPagamento, p.ValorTotal)).ToList());
    }

    /// <summary>Altera o status via sp_AtualizarStatusPedido (transições validadas por trigger).</summary>
    [HttpPut("pedidos/{id:int}/status")]
    public async Task<IActionResult> AtualizarStatus(int id, [FromBody] StatusPedidoRequest request)
    {
        if (!_db.Database.IsSqlServer()) return RequerSqlServer();
        try
        {
            var linhas = await _db.QueryProcedureAsync("dbo.sp_AtualizarStatusPedido",
                new Dictionary<string, object?> { ["PedidoId"] = id, ["NovoStatus"] = request.Status });
            return Ok(linhas);
        }
        catch (ProcedureException ex)
        {
            return FalhaDeProcedimento(ex);
        }
    }

    [HttpGet("relatorios/vendas")]
    public async Task<IActionResult> Vendas([FromQuery] DateTime? de, [FromQuery] DateTime? ate)
    {
        if (!_db.Database.IsSqlServer()) return RequerSqlServer();
        try
        {
            return Ok(await _db.QueryProcedureAsync("dbo.sp_RelatorioVendasPorPeriodo",
                new Dictionary<string, object?> { ["De"] = de?.Date, ["Ate"] = ate?.Date }));
        }
        catch (ProcedureException ex) { return FalhaDeProcedimento(ex); }
    }

    [HttpGet("relatorios/mais-vendidos")]
    public async Task<IActionResult> MaisVendidos([FromQuery] int top = 10, [FromQuery] DateTime? de = null, [FromQuery] DateTime? ate = null)
    {
        if (!_db.Database.IsSqlServer()) return RequerSqlServer();
        try
        {
            return Ok(await _db.QueryProcedureAsync("dbo.sp_ProdutosMaisVendidos",
                new Dictionary<string, object?> { ["Top"] = top, ["De"] = de?.Date, ["Ate"] = ate?.Date }));
        }
        catch (ProcedureException ex) { return FalhaDeProcedimento(ex); }
    }

    [HttpGet("estoque/baixo")]
    public async Task<IActionResult> EstoqueBaixo()
    {
        if (!_db.Database.IsSqlServer()) return RequerSqlServer();
        try
        {
            return Ok(await _db.QueryProcedureAsync("dbo.sp_ProdutosEstoqueBaixo", null));
        }
        catch (ProcedureException ex) { return FalhaDeProcedimento(ex); }
    }

    [HttpPost("estoque/ajustes")]
    public async Task<IActionResult> AjustarEstoque([FromBody] AjusteEstoqueRequest request)
    {
        if (!_db.Database.IsSqlServer()) return RequerSqlServer();
        try
        {
            return Ok(await _db.QueryProcedureAsync("dbo.sp_AjustarEstoque", new Dictionary<string, object?>
            {
                ["ProdutoId"] = request.ProdutoId,
                ["Tipo"] = request.Tipo,
                ["Quantidade"] = request.Quantidade,
                ["Motivo"] = request.Motivo,
                ["UsuarioId"] = UsuarioIdAtual
            }));
        }
        catch (ProcedureException ex) { return FalhaDeProcedimento(ex); }
    }
}
