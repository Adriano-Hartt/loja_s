using loja_s.Data;
using loja_s.Models;
using loja_s.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace loja_s.Api;

/// <summary>Catálogo público de produtos.</summary>
[Route("api/v1/produtos")]
[Produces("application/json")]
public class ProdutosApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public ProdutosApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Lista produtos ativos, com busca por nome/SKU/edição e paginação.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PaginaDto<ProdutoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? busca,
        [FromQuery] bool apenasDisponiveis = false,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 20)
    {
        pagina = Math.Max(pagina, 1);
        tamanho = Math.Clamp(tamanho, 1, 50);

        var consulta = _db.Produtos.AsNoTracking().Where(p => p.Status == "Ativo");

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            consulta = consulta.Where(p =>
                EF.Functions.Like(p.Nome, termo) ||
                EF.Functions.Like(p.SKU, termo) ||
                (p.EdicaoEspecial != null && EF.Functions.Like(p.EdicaoEspecial, termo)));
        }

        if (apenasDisponiveis)
        {
            consulta = consulta.Where(p => p.Estoque > 0);
        }

        var total = await consulta.CountAsync();
        var produtos = await consulta
            .OrderBy(p => p.Nome)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync();

        return Ok(new PaginaDto<ProdutoDto>(
            produtos.Select(p => p.ParaDto(Request)).ToList(), pagina, tamanho, total));
    }

    /// <summary>Detalhes de um produto ativo.</summary>
    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProdutoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(int id)
    {
        var produto = await _db.Produtos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.Status == "Ativo");
        return produto == null
            ? Falha(StatusCodes.Status404NotFound, "Produto não encontrado.")
            : Ok(produto.ParaDto(Request));
    }
}

/// <summary>Carrinho persistido na conta do cliente (reaproveita o CarrinhoService do site).</summary>
[Route("api/v1/carrinho")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
[Produces("application/json")]
public class CarrinhoApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly CarrinhoService _carrinho;

    public CarrinhoApiController(ApplicationDbContext db, CarrinhoService carrinho)
    {
        _db = db;
        _carrinho = carrinho;
    }

    /// <summary>Carrinho atual, já ajustado a preço e estoque vigentes.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CarrinhoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obter()
    {
        var atualizado = await _carrinho.SincronizarComCatalogoAsync(UsuarioIdAtual);
        return Ok(await MontarAsync(atualizado));
    }

    [HttpPost("itens")]
    [ProducesResponseType(typeof(CarrinhoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Adicionar([FromBody] ItemCarrinhoRequest request)
    {
        try
        {
            await _carrinho.AdicionarProdutoAsync(request.ProdutoId, request.Quantidade, UsuarioIdAtual);
        }
        catch (InvalidOperationException ex)
        {
            return Falha(StatusCodes.Status409Conflict, "Não foi possível adicionar o produto.", ex.Message);
        }

        return Ok(await MontarAsync());
    }

    [HttpPut("itens/{produtoId:int}")]
    [ProducesResponseType(typeof(CarrinhoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Atualizar(int produtoId, [FromBody] QuantidadeRequest request)
    {
        try
        {
            await _carrinho.AtualizarQuantidadeAsync(produtoId, request.Quantidade, UsuarioIdAtual);
        }
        catch (InvalidOperationException ex)
        {
            return Falha(StatusCodes.Status409Conflict, "Não foi possível atualizar a quantidade.", ex.Message);
        }

        return Ok(await MontarAsync());
    }

    [HttpDelete("itens/{produtoId:int}")]
    [ProducesResponseType(typeof(CarrinhoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Remover(int produtoId)
    {
        await _carrinho.RemoverProdutoAsync(produtoId, UsuarioIdAtual);
        return Ok(await MontarAsync());
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Limpar()
    {
        await _carrinho.LimparAsync(UsuarioIdAtual);
        return NoContent();
    }

    private async Task<CarrinhoDto> MontarAsync(bool catalogoAtualizado = false)
    {
        var itens = await _carrinho.ObterItensAsync(UsuarioIdAtual);
        var ids = itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await _db.Produtos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var dtos = itens.Select(i =>
        {
            produtos.TryGetValue(i.ProdutoId, out var produto);
            return new ItemCarrinhoDto(
                i.ProdutoId,
                i.NomeProduto,
                i.Quantidade,
                i.PrecoUnitario,
                i.Subtotal,
                Midia(produto?.ImagemUrl),
                produto?.Estoque ?? 0);
        }).ToList();

        var subtotal = dtos.Sum(i => i.Subtotal);
        var frete = subtotal > 0 ? Regras.FreteFixo : 0m;
        return new CarrinhoDto(dtos, dtos.Sum(i => i.Quantidade), subtotal, frete, subtotal + frete, catalogoAtualizado);
    }
}

/// <summary>Produtos favoritos do cliente.</summary>
[Route("api/v1/favoritos")]
[Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
[Produces("application/json")]
public class FavoritosApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;

    public FavoritosApiController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FavoritoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar()
    {
        var usuarioId = UsuarioIdAtual;
        var favoritos = await _db.Favoritos.AsNoTracking()
            .Include(f => f.Produto)
            .Where(f => f.UsuarioId == usuarioId && f.Produto.Status == "Ativo")
            .OrderByDescending(f => f.DataAdicionado)
            .ToListAsync();

        return Ok(favoritos
            .Select(f => new FavoritoDto(f.ProdutoId, ApiMapper.Utc(f.DataAdicionado), f.Produto.ParaDto(Request)))
            .ToList());
    }

    /// <summary>Marca como favorito (idempotente).</summary>
    [HttpPut("{produtoId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Adicionar(int produtoId)
    {
        var usuarioId = UsuarioIdAtual;
        var existe = await _db.Produtos.AnyAsync(p => p.Id == produtoId && p.Status == "Ativo");
        if (!existe)
        {
            return Falha(StatusCodes.Status404NotFound, "Produto não encontrado.");
        }

        if (!await _db.Favoritos.AnyAsync(f => f.UsuarioId == usuarioId && f.ProdutoId == produtoId))
        {
            _db.Favoritos.Add(new Favorito { UsuarioId = usuarioId, ProdutoId = produtoId });
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>Remove dos favoritos (idempotente).</summary>
    [HttpDelete("{produtoId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remover(int produtoId)
    {
        var usuarioId = UsuarioIdAtual;
        var favorito = await _db.Favoritos
            .FirstOrDefaultAsync(f => f.UsuarioId == usuarioId && f.ProdutoId == produtoId);
        if (favorito != null)
        {
            _db.Favoritos.Remove(favorito);
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }
}
