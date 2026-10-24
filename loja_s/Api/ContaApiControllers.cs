using loja_s.Data;
using loja_s.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace loja_s.Api;

/// <summary>Resumo da conta do cliente.</summary>
[Route("api/v1/conta")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
[Produces("application/json")]
public class ContaApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public ContaApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Indicadores da conta. Com SQL Server vêm da procedure sp_ObterResumoCliente;
    /// com SQLite (desenvolvimento) são calculados por consulta equivalente.
    /// </summary>
    [HttpGet("resumo")]
    [ProducesResponseType(typeof(ResumoContaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resumo()
    {
        var usuarioId = UsuarioIdAtual;

        if (_db.Database.IsSqlServer())
        {
            try
            {
                var linhas = await _db.QueryProcedureAsync("dbo.sp_ObterResumoCliente",
                    new Dictionary<string, object?> { ["UsuarioId"] = usuarioId });
                var linha = linhas.FirstOrDefault();
                if (linha == null)
                {
                    return Falha(StatusCodes.Status404NotFound, "Usuário não encontrado.");
                }

                return Ok(new ResumoContaDto(
                    linha.Inteiro("QuantidadePedidos"),
                    linha.Inteiro("PedidosEmAberto"),
                    linha.Decimal("TotalGasto"),
                    linha.Inteiro("Favoritos"),
                    linha.Inteiro("NotificacoesNaoLidas"),
                    linha.Inteiro("ItensNoCarrinho")));
            }
            catch (ProcedureException ex)
            {
                return FalhaDeProcedimento(ex);
            }
        }

        var emAberto = new[] { "Pendente", "AguardandoPagamento", "Finalizado", "Enviado" };
        var pedidos = await _db.Pedidos.AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId && p.Status != "Cancelado")
            .Select(p => new { p.Status, p.StatusPagamento, p.ValorTotal })
            .ToListAsync();

        var itensCarrinho = await _db.ItensCarrinho.AsNoTracking()
            .Where(i => _db.Carrinhos.Any(c => c.Id == i.CarrinhoId && c.UsuarioId == usuarioId))
            .Select(i => i.Quantidade)
            .ToListAsync();

        return Ok(new ResumoContaDto(
            pedidos.Count,
            pedidos.Count(p => emAberto.Contains(p.Status)),
            pedidos.Where(p => p.StatusPagamento == "Pago").Sum(p => p.ValorTotal),
            await _db.Favoritos.CountAsync(f => f.UsuarioId == usuarioId),
            await _db.Notificacoes.CountAsync(n => n.UsuarioId == usuarioId && n.LidaEm == null),
            itensCarrinho.Sum()));
    }
}

/// <summary>Endereços de entrega do cliente.</summary>
[Route("api/v1/enderecos")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
[Produces("application/json")]
public class EnderecosApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public EnderecosApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EnderecoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar()
    {
        var usuarioId = UsuarioIdAtual;
        var enderecos = await _db.Enderecos.AsNoTracking()
            .Where(e => e.UsuarioId == usuarioId)
            .OrderByDescending(e => e.Principal)
            .ThenByDescending(e => e.Id)
            .ToListAsync();
        return Ok(enderecos.Select(e => e.ParaDto()).ToList());
    }

    [HttpPost]
    [ProducesResponseType(typeof(EnderecoDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Criar([FromBody] EnderecoRequest request)
    {
        var usuarioId = UsuarioIdAtual;
        var jaTem = await _db.Enderecos.AnyAsync(e => e.UsuarioId == usuarioId);
        var principal = request.Principal || !jaTem;

        if (principal)
        {
            var atuais = await _db.Enderecos.Where(e => e.UsuarioId == usuarioId && e.Principal).ToListAsync();
            foreach (var atual in atuais)
            {
                atual.Principal = false;
            }

            await _db.SaveChangesAsync();
        }

        var endereco = new Endereco
        {
            UsuarioId = usuarioId,
            Nome = request.Nome.Trim(),
            CEP = request.Cep.Trim(),
            Rua = request.Rua.Trim(),
            Numero = request.Numero.Trim(),
            Complemento = request.Complemento?.Trim(),
            Bairro = request.Bairro.Trim(),
            Cidade = request.Cidade.Trim(),
            Estado = request.Estado.Trim().ToUpperInvariant(),
            Pais = string.IsNullOrWhiteSpace(request.Pais) ? "Brasil" : request.Pais.Trim(),
            Principal = principal
        };
        _db.Enderecos.Add(endereco);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, endereco.ParaDto());
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(int id)
    {
        var usuarioId = UsuarioIdAtual;
        var endereco = await _db.Enderecos.FirstOrDefaultAsync(e => e.Id == id && e.UsuarioId == usuarioId);
        if (endereco == null)
        {
            return Falha(StatusCodes.Status404NotFound, "Endereço não encontrado.");
        }

        if (await _db.Pedidos.AnyAsync(p => p.EnderecoEntregaId == id))
        {
            return Falha(StatusCodes.Status409Conflict, "Endereço em uso.",
                "Este endereço está associado a um pedido e não pode ser removido.");
        }

        var eraPrincipal = endereco.Principal;
        _db.Enderecos.Remove(endereco);
        await _db.SaveChangesAsync();

        if (eraPrincipal)
        {
            var proximo = await _db.Enderecos.Where(e => e.UsuarioId == usuarioId).OrderBy(e => e.Id).FirstOrDefaultAsync();
            if (proximo != null)
            {
                proximo.Principal = true;
                await _db.SaveChangesAsync();
            }
        }

        return NoContent();
    }
}

/// <summary>Notificações do cliente (inclui as geradas pelos triggers do banco).</summary>
[Route("api/v1/notificacoes")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
[Produces("application/json")]
public class NotificacoesApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public NotificacoesApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificacaoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] bool apenasNaoLidas = false)
    {
        var usuarioId = UsuarioIdAtual;
        var consulta = _db.Notificacoes.AsNoTracking().Where(n => n.UsuarioId == usuarioId);
        if (apenasNaoLidas)
        {
            consulta = consulta.Where(n => n.LidaEm == null);
        }

        var notificacoes = await consulta.OrderByDescending(n => n.CriadaEm).Take(50).ToListAsync();
        return Ok(notificacoes.Select(n => new NotificacaoDto(
            n.Id, n.Categoria, n.Titulo, n.Mensagem, ApiMapper.Utc(n.CriadaEm), n.LidaEm != null)).ToList());
    }

    [HttpPost("{id:int}/lida")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarComoLida(int id)
    {
        var usuarioId = UsuarioIdAtual;
        var notificacao = await _db.Notificacoes.FirstOrDefaultAsync(n => n.Id == id && n.UsuarioId == usuarioId);
        if (notificacao == null)
        {
            return Falha(StatusCodes.Status404NotFound, "Notificação não encontrada.");
        }

        notificacao.LidaEm ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("lidas")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarcarTodasComoLidas()
    {
        var usuarioId = UsuarioIdAtual;
        var agora = DateTime.UtcNow;
        await _db.Notificacoes
            .Where(n => n.UsuarioId == usuarioId && n.LidaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.LidaEm, agora));
        return NoContent();
    }
}
