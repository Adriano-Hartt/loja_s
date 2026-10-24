using loja_s.Data;
using loja_s.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace loja_s.Controllers;

/// <summary>Painel administrativo (perfil Admin): dashboard, pedidos, produtos/estoque e relatórios.</summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    // Mesmas transições validadas pelo trigger trg_Pedidos_ControlaStatus no SQL Server.
    private static readonly Dictionary<string, string[]> Transicoes = new()
    {
        ["Pendente"] = new[] { "AguardandoPagamento", "Finalizado", "Cancelado" },
        ["AguardandoPagamento"] = new[] { "Finalizado", "Cancelado" },
        ["Finalizado"] = new[] { "Enviado", "Cancelado" },
        ["Enviado"] = new[] { "Entregue" },
        ["Entregue"] = Array.Empty<string>(),
        ["Cancelado"] = Array.Empty<string>()
    };

    private readonly ApplicationDbContext _context;

    public AdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    private int UsuarioId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public async Task<IActionResult> Index()
    {
        var desde = DateTime.UtcNow.AddDays(-30);
        var pedidos = await _context.Pedidos.AsNoTracking()
            .Where(p => p.DataPedido >= desde && p.Status != "Cancelado")
            .Select(p => new { p.ValorTotal, p.Status })
            .ToListAsync();

        var model = new AdminDashboardViewModel
        {
            Pedidos30Dias = pedidos.Count,
            Faturamento30Dias = pedidos.Sum(p => p.ValorTotal),
            PedidosEmAberto = await _context.Pedidos.CountAsync(p =>
                p.Status == "Pendente" || p.Status == "AguardandoPagamento" || p.Status == "Finalizado"),
            Clientes = await _context.Usuarios.CountAsync(u => u.Tipo == "Cliente"),
            ProdutosAtivos = await _context.Produtos.CountAsync(p => p.Status == "Ativo"),
            EstoqueBaixo = await _context.Produtos.AsNoTracking()
                .Where(p => p.Status == "Ativo" && p.Estoque <= p.EstoqueMinimo)
                .OrderBy(p => p.Estoque).Take(10).ToListAsync(),
            UltimosPedidos = await _context.Pedidos.AsNoTracking().Include(p => p.Usuario)
                .OrderByDescending(p => p.DataPedido).Take(5).ToListAsync()
        };
        return View(model);
    }

    // ---------------------------------------------------------------- pedidos

    public async Task<IActionResult> Pedidos(string? status)
    {
        var consulta = _context.Pedidos.AsNoTracking()
            .Include(p => p.Usuario).Include(p => p.Pagamento).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            consulta = consulta.Where(p => p.Status == status);
        }

        ViewBag.Status = status;
        ViewBag.Todos = Transicoes.Keys.ToList();
        return View(await consulta.OrderByDescending(p => p.DataPedido).Take(200).ToListAsync());
    }

    public async Task<IActionResult> Pedido(int id)
    {
        var pedido = await CarregarPedidoAsync(id);
        if (pedido == null) return NotFound();
        ViewBag.Proximos = Transicoes.GetValueOrDefault(pedido.Status) ?? Array.Empty<string>();
        return View(pedido);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AlterarStatus(int id, string novoStatus)
    {
        var pedido = await _context.Pedidos.Include(p => p.Itens).Include(p => p.Pagamento)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (pedido == null) return NotFound();

        if (!(Transicoes.GetValueOrDefault(pedido.Status) ?? Array.Empty<string>()).Contains(novoStatus))
        {
            TempData["Erro"] = $"Transição inválida: {pedido.Status} → {novoStatus}.";
            return RedirectToAction(nameof(Pedido), new { id });
        }

        if (_context.Database.IsSqlServer())
        {
            // Procedure + triggers cuidam de estoque, pagamento e notificação ao cliente.
            try
            {
                await _context.QueryProcedureAsync("dbo.sp_AtualizarStatusPedido",
                    new Dictionary<string, object?> { ["PedidoId"] = id, ["NovoStatus"] = novoStatus });
            }
            catch (ProcedureException ex)
            {
                TempData["Erro"] = ex.Message;
                return RedirectToAction(nameof(Pedido), new { id });
            }
        }
        else
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            if (novoStatus == "Cancelado")
            {
                await DevolverEstoqueAsync(pedido);
                if (pedido.Pagamento != null)
                {
                    pedido.Pagamento.Status = pedido.Pagamento.Status == "Pago" ? "Estornado" : "Cancelado";
                }
                pedido.StatusPagamento = pedido.Pagamento?.Status ?? pedido.StatusPagamento;
            }
            pedido.Status = novoStatus;
            _context.Notificacoes.Add(new Notificacao
            {
                UsuarioId = pedido.UsuarioId,
                Categoria = "Pedido",
                Titulo = $"Pedido {pedido.NumeroPedido}",
                Mensagem = $"Seu pedido agora está: {novoStatus}."
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        TempData["Sucesso"] = $"Status atualizado para {novoStatus}.";
        return RedirectToAction(nameof(Pedido), new { id });
    }

    private async Task DevolverEstoqueAsync(Pedido pedido)
    {
        foreach (var item in pedido.Itens)
        {
            var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == item.ProdutoId);
            if (produto == null) continue;
            var anterior = produto.Estoque;
            produto.Estoque += item.Quantidade;
            produto.DataAtualizacao = DateTime.UtcNow;
            _context.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = produto.Id,
                TipoMovimentacao = "Entrada",
                Quantidade = item.Quantidade,
                EstoqueAnterior = anterior,
                EstoqueAtual = produto.Estoque,
                Motivo = $"Cancelamento do pedido {pedido.NumeroPedido}",
                UsuarioId = UsuarioId
            });
        }
    }

    private Task<Pedido?> CarregarPedidoAsync(int id) =>
        _context.Pedidos.AsNoTracking()
            .Include(p => p.Usuario).Include(p => p.Pagamento)
            .Include(p => p.EnderecoEntrega).Include(p => p.Itens)
            .FirstOrDefaultAsync(p => p.Id == id);

    // ---------------------------------------------------------------- produtos / estoque

    public async Task<IActionResult> Produtos(string? busca)
    {
        var consulta = _context.Produtos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            consulta = consulta.Where(p => EF.Functions.Like(p.Nome, termo) || EF.Functions.Like(p.SKU, termo));
        }

        ViewBag.Busca = busca;
        return View(await consulta.OrderBy(p => p.Nome).ToListAsync());
    }

    public async Task<IActionResult> EditarProduto(int id)
    {
        var produto = await _context.Produtos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (produto == null) return NotFound();
        return View(new EditarProdutoViewModel
        {
            Id = produto.Id, Nome = produto.Nome, SKU = produto.SKU, Preco = produto.Preco,
            PrecoPromocional = produto.PrecoPromocional, Descricao = produto.Descricao,
            EstoqueMinimo = produto.EstoqueMinimo, Status = produto.Status, Estoque = produto.Estoque
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarProduto(EditarProdutoViewModel model)
    {
        if (model.PrecoPromocional is > 0 && model.PrecoPromocional >= model.Preco)
        {
            ModelState.AddModelError(nameof(model.PrecoPromocional), "O preço promocional deve ser menor que o preço.");
        }

        if (!ModelState.IsValid) return View(model);

        var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == model.Id);
        if (produto == null) return NotFound();

        produto.Nome = model.Nome.Trim();
        produto.Preco = model.Preco;
        produto.PrecoPromocional = model.PrecoPromocional is > 0 ? model.PrecoPromocional : null;
        produto.Descricao = model.Descricao ?? string.Empty;
        produto.EstoqueMinimo = model.EstoqueMinimo;
        produto.Status = model.Status;
        produto.DataAtualizacao = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["Sucesso"] = "Produto atualizado.";
        return RedirectToAction(nameof(Produtos));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AjustarEstoque(int id, string tipo, int quantidade, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || quantidade < 0 || tipo is not ("Entrada" or "Saida" or "Ajuste"))
        {
            TempData["Erro"] = "Informe tipo, quantidade e motivo válidos.";
            return RedirectToAction(nameof(EditarProduto), new { id });
        }

        if (_context.Database.IsSqlServer())
        {
            try
            {
                await _context.QueryProcedureAsync("dbo.sp_AjustarEstoque", new Dictionary<string, object?>
                {
                    ["ProdutoId"] = id, ["Tipo"] = tipo, ["Quantidade"] = quantidade,
                    ["Motivo"] = motivo.Trim(), ["UsuarioId"] = UsuarioId
                });
            }
            catch (ProcedureException ex)
            {
                TempData["Erro"] = ex.Message;
                return RedirectToAction(nameof(EditarProduto), new { id });
            }
        }
        else
        {
            var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == id);
            if (produto == null) return NotFound();
            var anterior = produto.Estoque;
            var novo = tipo switch
            {
                "Entrada" => anterior + quantidade,
                "Saida" => anterior - quantidade,
                _ => quantidade
            };
            if (novo < 0)
            {
                TempData["Erro"] = "Estoque insuficiente para essa saída.";
                return RedirectToAction(nameof(EditarProduto), new { id });
            }

            produto.Estoque = novo;
            produto.DataAtualizacao = DateTime.UtcNow;
            _context.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                ProdutoId = id, TipoMovimentacao = tipo, Quantidade = quantidade, EstoqueAnterior = anterior,
                EstoqueAtual = novo, Motivo = motivo.Trim(), UsuarioId = UsuarioId
            });
            await _context.SaveChangesAsync();
        }

        TempData["Sucesso"] = "Estoque ajustado.";
        return RedirectToAction(nameof(EditarProduto), new { id });
    }

    // ---------------------------------------------------------------- relatórios

    public async Task<IActionResult> Relatorios(DateTime? de, DateTime? ate)
    {
        var fim = (ate ?? DateTime.UtcNow.Date).Date.AddDays(1);
        var inicio = (de ?? fim.AddDays(-31)).Date;

        var pedidos = await _context.Pedidos.AsNoTracking()
            .Where(p => p.DataPedido >= inicio && p.DataPedido < fim && p.Status != "Cancelado")
            .Select(p => new { p.DataPedido, p.ValorTotal })
            .ToListAsync();

        var itens = await _context.ItensPedido.AsNoTracking()
            .Where(i => i.Pedido.DataPedido >= inicio && i.Pedido.DataPedido < fim && i.Pedido.Status != "Cancelado")
            .Select(i => new { i.ProdutoId, i.NomeProduto, i.Quantidade, i.PrecoUnitario })
            .ToListAsync();

        return View(new RelatorioViewModel
        {
            De = inicio,
            Ate = fim.AddDays(-1),
            VendasPorDia = pedidos.GroupBy(p => p.DataPedido.Date).OrderBy(g => g.Key)
                .Select(g => (g.Key, g.Count(), g.Sum(x => x.ValorTotal))).ToList(),
            MaisVendidos = itens.GroupBy(i => new { i.ProdutoId, i.NomeProduto })
                .Select(g => (g.Key.NomeProduto, g.Sum(x => x.Quantidade), g.Sum(x => x.Quantidade * x.PrecoUnitario)))
                .OrderByDescending(x => x.Item2).Take(10).ToList()
        });
    }
}

public class AdminDashboardViewModel
{
    public int Pedidos30Dias { get; set; }
    public decimal Faturamento30Dias { get; set; }
    public int PedidosEmAberto { get; set; }
    public int Clientes { get; set; }
    public int ProdutosAtivos { get; set; }
    public List<Produto> EstoqueBaixo { get; set; } = new();
    public List<Pedido> UltimosPedidos { get; set; } = new();
}

public class EditarProdutoViewModel
{
    public int Id { get; set; }
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)]
    public string Nome { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    [System.ComponentModel.DataAnnotations.Range(0.01, 1_000_000)]
    public decimal Preco { get; set; }
    public decimal? PrecoPromocional { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(1000)]
    public string? Descricao { get; set; }
    [System.ComponentModel.DataAnnotations.Range(0, 100000)]
    public int EstoqueMinimo { get; set; }
    [System.ComponentModel.DataAnnotations.RegularExpression("^(Ativo|Inativo|Descontinuado)$")]
    public string Status { get; set; } = "Ativo";
    public int Estoque { get; set; }
}

public class RelatorioViewModel
{
    public DateTime De { get; set; }
    public DateTime Ate { get; set; }
    public List<(DateTime Dia, int Pedidos, decimal Total)> VendasPorDia { get; set; } = new();
    public List<(string Produto, int Quantidade, decimal Total)> MaisVendidos { get; set; } = new();
}
