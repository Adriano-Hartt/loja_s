using loja_s.Data;
using loja_s.Models;
using loja_s.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace loja_s.Controllers;

public class CarrinhoController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly CarrinhoService _carrinhoService;

    public CarrinhoController(ApplicationDbContext context, CarrinhoService carrinhoService)
    {
        _context = context;
        _carrinhoService = carrinhoService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = ObterUsuarioId();
        ViewBag.CatalogoAtualizado = await _carrinhoService.SincronizarComCatalogoAsync(userId);
        var itens = await _carrinhoService.ObterItensAsync(userId);
        var subtotal = itens.Sum(i => i.Quantidade * i.PrecoUnitario);
        var frete = subtotal > 0 ? 19.90m : 0m;
        var total = subtotal + frete;

        ViewBag.Subtotal = subtotal;
        ViewBag.Frete = frete;
        ViewBag.Total = total;

        // Foto de cada produto (a mesma da página do produto) para mostrar no carrinho.
        var idsProdutos = itens.Select(i => i.ProdutoId).Distinct().ToList();
        ViewBag.Imagens = await _context.Produtos.AsNoTracking()
            .Where(p => idsProdutos.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ImagemUrl);
        return View(itens);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adicionar(int produtoId, int quantidade = 1, string returnUrl = "/")
    {
        var produto = _context.Produtos.FirstOrDefault(p => p.Id == produtoId);
        if (produto == null)
        {
            return NotFound();
        }

        if (quantidade <= 0)
        {
            TempData["ErrorMessage"] = "A quantidade deve ser maior que zero.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _carrinhoService.AdicionarProdutoAsync(produtoId, quantidade, ObterUsuarioId());
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = $"{produto.Nome} foi adicionado ao carrinho.";
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarQuantidade(int produtoId, int quantidade)
    {
        try
        {
            await _carrinhoService.AtualizarQuantidadeAsync(produtoId, quantidade, ObterUsuarioId());
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = "Carrinho atualizado.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Atualização automática da quantidade (chamada pelo JavaScript do carrinho, sem recarregar a página).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarQuantidadeAjax(int produtoId, int quantidade)
    {
        var userId = ObterUsuarioId();
        string? erro = null;
        try
        {
            await _carrinhoService.AtualizarQuantidadeAsync(produtoId, quantidade, userId);
        }
        catch (InvalidOperationException exception)
        {
            erro = exception.Message;
        }

        var itens = await _carrinhoService.ObterItensAsync(userId);
        var item = itens.FirstOrDefault(i => i.ProdutoId == produtoId);
        var subtotal = itens.Sum(i => i.Quantidade * i.PrecoUnitario);
        var frete = subtotal > 0 ? 19.90m : 0m;

        var resposta = new
        {
            erro,
            removido = item == null,
            quantidade = item?.Quantidade ?? 0,
            subtotalItem = item?.Subtotal ?? 0m,
            subtotal,
            frete,
            total = subtotal + frete
        };
        return erro == null ? Json(resposta) : BadRequest(resposta);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remover(int produtoId)
    {
        await _carrinhoService.RemoverProdutoAsync(produtoId, ObterUsuarioId());
        TempData["SuccessMessage"] = "Produto removido do carrinho.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarParaDepois(int produtoId)
    {
        var usuarioId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId && p.Status == "Ativo");
        if (produto == null)
        {
            return NotFound();
        }

        if (!await _context.Favoritos.AnyAsync(f => f.UsuarioId == usuarioId && f.ProdutoId == produtoId))
        {
            _context.Favoritos.Add(new Models.Favorito { UsuarioId = usuarioId, ProdutoId = produtoId });
            await _context.SaveChangesAsync();
        }

        await _carrinhoService.RemoverProdutoAsync(produtoId, usuarioId);
        TempData["ContaMensagem"] = "Produto salvo nos favoritos para depois.";
        return RedirectToAction(nameof(Index));
    }

    private int? ObterUsuarioId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
