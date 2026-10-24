using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using loja_s.Data;
using loja_s.Models;
using loja_s.Services;
using loja_s.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace loja_s.Controllers;

public class ContaController : Controller
{
    private const string ResetPasswordPurpose = "reset-password";
    private const string ConfirmEmailPurpose = "confirm-email";
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly IContaEmailService _emailService;
    private readonly CarrinhoService _carrinhoService;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public ContaController(
        ApplicationDbContext context,
        IPasswordHasher<Usuario> passwordHasher,
        IContaEmailService emailService,
        CarrinhoService carrinhoService,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _carrinhoService = carrinhoService;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Cadastro(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(MinhaConta));
        }

        return View(new CadastroContaViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cadastro(CadastroContaViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = NormalizarEmail(model.Email);
        if (await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == email))
        {
            ModelState.AddModelError(nameof(model.Email), "Este e-mail já está cadastrado.");
            return View(model);
        }

        var usuario = new Usuario
        {
            Nome = model.Nome.Trim(),
            Email = email,
            Telefone = model.Telefone?.Trim(),
            CPF = string.Empty,
            Tipo = "Cliente",
            Status = "Ativo",
            DataCadastro = DateTime.UtcNow
        };

        await using var transaction = await _context.Database.BeginTransactionAsync();
        _context.Usuarios.Add(usuario);
        await _context.SaveChangesAsync();

        usuario.SenhaHash = _passwordHasher.HashPassword(usuario, model.Senha);
        usuario.PerfilConta = new PerfilConta
        {
            UsuarioId = usuario.Id,
            Sobrenome = model.Sobrenome.Trim(),
            DataNascimento = model.DataNascimento,
            TermosAceitosEm = DateTime.UtcNow
        };
        usuario.SegurancaConta = new SegurancaConta { UsuarioId = usuario.Id };
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["ContaMensagem"] = "Conta criada. Entre com seu e-mail e senha para continuar.";
        return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(MinhaConta));
        }

        return View(new LoginContaViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginContaViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = NormalizarEmail(model.Email);
        var usuario = await _context.Usuarios
            .Include(u => u.SegurancaConta)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && (u.Tipo == "Cliente" || u.Tipo == "Admin"));

        if (usuario == null || usuario.Status != "Ativo")
        {
            ModelState.AddModelError(string.Empty, "E-mail ou senha inválidos.");
            return View(model);
        }

        var seguranca = usuario.SegurancaConta;
        if (seguranca == null)
        {
            seguranca = new SegurancaConta { UsuarioId = usuario.Id };
            _context.SegurancasConta.Add(seguranca);
        }

        if (seguranca.BloqueadoAte > DateTime.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "Acesso temporariamente bloqueado. Aguarde e tente novamente.");
            return View(model);
        }

        var resultado = _passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, model.Senha);
        if (resultado == PasswordVerificationResult.Failed)
        {
            seguranca.TentativasFalhas++;
            if (seguranca.TentativasFalhas >= 5)
            {
                seguranca.TentativasFalhas = 0;
                seguranca.BloqueadoAte = DateTime.UtcNow.AddMinutes(15);
            }

            await _context.SaveChangesAsync();
            ModelState.AddModelError(string.Empty, "E-mail ou senha inválidos.");
            return View(model);
        }

        seguranca.TentativasFalhas = 0;
        seguranca.BloqueadoAte = null;
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            usuario.SenhaHash = _passwordHasher.HashPassword(usuario, model.Senha);
        }

        var now = DateTime.UtcNow;
        var inactiveSessions = await _context.SessoesConta
            .Where(s => s.UsuarioId == usuario.Id && s.UltimaAtividade < now.AddDays(-30))
            .ToListAsync();
        _context.SessoesConta.RemoveRange(inactiveSessions);

        var sessionKey = Guid.NewGuid().ToString("N");
        var session = new SessaoConta
        {
            UsuarioId = usuario.Id,
            ChaveSessao = sessionKey,
            Dispositivo = Request.Headers.UserAgent.ToString().Trim(),
            CriadaEm = now,
            UltimaAtividade = now
        };
        if (session.Dispositivo.Length > 250)
        {
            session.Dispositivo = session.Dispositivo[..250];
        }

        _context.SessoesConta.Add(session);
        await _context.SaveChangesAsync();

        var cartStockAdjusted = await _carrinhoService.MesclarCarrinhoDaSessaoAsync(usuario.Id);
        if (cartStockAdjusted)
        {
            TempData["ErroConta"] = "Alguns itens do carrinho foram removidos ou ajustados porque o estoque mudou.";
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Name, usuario.Nome),
            new Claim(ClaimTypes.Role, usuario.Tipo),
            new Claim("AccountSecurityStamp", seguranca.SecurityStamp),
            new Claim("AccountSessionId", sessionKey)
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties
        {
            IsPersistent = model.ManterConectado,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(model.ManterConectado ? 30 * 24 : 8)
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        if (usuario.Tipo == "Admin")
        {
            return RedirectToAction("Index", "Admin");
        }

        return RedirectToAction(nameof(MinhaConta));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        var sessionId = User.FindFirstValue("AccountSessionId");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await _context.SessoesConta.FirstOrDefaultAsync(s => s.ChaveSessao == sessionId);
            if (session != null)
            {
                _context.SessoesConta.Remove(session);
                await _context.SaveChangesAsync();
            }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MinhaConta()
    {
        var usuarioId = UsuarioIdAtual();
        var usuario = await _context.Usuarios.Include(u => u.PerfilConta)
            .FirstOrDefaultAsync(u => u.Id == usuarioId);
        if (usuario == null)
        {
            return Challenge();
        }

        ViewBag.Pedidos = await _context.Pedidos.CountAsync(p => p.UsuarioId == usuarioId);
        ViewBag.Enderecos = await _context.Enderecos.CountAsync(e => e.UsuarioId == usuarioId);
        ViewBag.Favoritos = await _context.Favoritos.CountAsync(f => f.UsuarioId == usuarioId);
        ViewBag.Carrinho = await _carrinhoService.ObterQuantidadeTotalAsync(usuarioId);
        return View(usuario);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Dados()
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario == null)
        {
            return Challenge();
        }

        return View(new DadosContaViewModel
        {
            Nome = usuario.Nome,
            Sobrenome = usuario.PerfilConta?.Sobrenome ?? string.Empty,
            Email = usuario.Email,
            Telefone = usuario.Telefone,
            DataNascimento = usuario.PerfilConta?.DataNascimento,
            CPF = usuario.CPF
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dados(DadosContaViewModel model)
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario == null)
        {
            return Challenge();
        }

        var email = NormalizarEmail(model.Email);
        var emailAlterado = !string.Equals(usuario.Email, email, StringComparison.OrdinalIgnoreCase);
        var cpfAlterado = !string.Equals(usuario.CPF, model.CPF.Trim(), StringComparison.Ordinal);
        if (emailAlterado && !_emailService.IsAvailable)
        {
            ModelState.AddModelError(nameof(model.Email),
                "A confirmação por e-mail não está configurada. Tente novamente mais tarde.");
        }

        if ((emailAlterado || cpfAlterado) &&
            (string.IsNullOrWhiteSpace(model.SenhaAtual) ||
             _passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, model.SenhaAtual) == PasswordVerificationResult.Failed))
        {
            ModelState.AddModelError(nameof(model.SenhaAtual), "Confirme sua senha atual para alterar e-mail ou CPF.");
        }

        if (emailAlterado && await _context.Usuarios.AnyAsync(u => u.Id != usuario.Id && u.Email.ToLower() == email))
        {
            ModelState.AddModelError(nameof(model.Email), "Este e-mail já está em uso.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        usuario.Nome = model.Nome.Trim();
        usuario.Telefone = model.Telefone?.Trim();
        usuario.CPF = model.CPF.Trim();
        usuario.PerfilConta!.Sobrenome = model.Sobrenome.Trim();
        usuario.PerfilConta.DataNascimento = model.DataNascimento;
        if (emailAlterado)
        {
            usuario.Email = email;
            usuario.PerfilConta.EmailConfirmado = false;
        }

        await _context.SaveChangesAsync();

        if (emailAlterado)
        {
            try
            {
                await EnviarConfirmacaoEmailAsync(usuario);
                TempData["ContaMensagem"] = "Dados salvos. Enviamos um link para confirmar seu novo e-mail.";
            }
            catch (ContaEmailException exception)
            {
                TempData["ErroConta"] = exception.Message;
            }
        }
        else
        {
            TempData["ContaMensagem"] = "Seus dados foram atualizados.";
        }

        return RedirectToAction(nameof(Dados));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Notificacoes()
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario?.PerfilConta == null)
        {
            return Challenge();
        }

        return View(new PreferenciasNotificacaoViewModel
        {
            AtualizacoesPedidos = usuario.PerfilConta.AtualizacoesPedidos,
            Promocoes = usuario.PerfilConta.Promocoes,
            Novidades = usuario.PerfilConta.Novidades,
            ProdutosFavoritos = usuario.PerfilConta.ProdutosFavoritos,
            AlertasSeguranca = usuario.PerfilConta.AlertasSeguranca,
            Notificacoes = await ObterNotificacoesAsync(usuario.Id)
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Notificacoes(PreferenciasNotificacaoViewModel model)
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario?.PerfilConta == null)
        {
            return Challenge();
        }

        usuario.PerfilConta.AtualizacoesPedidos = model.AtualizacoesPedidos;
        usuario.PerfilConta.Promocoes = model.Promocoes;
        usuario.PerfilConta.Novidades = model.Novidades;
        usuario.PerfilConta.ProdutosFavoritos = model.ProdutosFavoritos;
        usuario.PerfilConta.AlertasSeguranca = model.AlertasSeguranca;
        await _context.SaveChangesAsync();
        TempData["ContaMensagem"] = "Preferências de comunicação salvas.";
        return RedirectToAction(nameof(Notificacoes));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarNotificacaoComoLida(int id)
    {
        var notificacao = await _context.Notificacoes.FirstOrDefaultAsync(n =>
            n.Id == id && n.UsuarioId == UsuarioIdAtual());
        if (notificacao == null)
        {
            return NotFound();
        }

        notificacao.LidaEm ??= DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Notificacoes));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Seguranca()
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario == null)
        {
            return Challenge();
        }

        var sessionId = User.FindFirstValue("AccountSessionId");
        ViewBag.Sessoes = await _context.SessoesConta
            .Where(s => s.UsuarioId == usuario.Id && s.UltimaAtividade >= DateTime.UtcNow.AddDays(-30))
            .OrderByDescending(s => s.UltimaAtividade).ToListAsync();
        ViewBag.SessaoAtual = sessionId;
        ViewBag.EmailConfirmado = usuario.PerfilConta?.EmailConfirmado ?? false;
        return View(new AlterarSenhaViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AlterarSenha(AlterarSenhaViewModel model)
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario == null)
        {
            return Challenge();
        }

        if (_passwordHasher.VerifyHashedPassword(usuario, usuario.SenhaHash, model.SenhaAtual) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(model.SenhaAtual), "A senha atual não confere.");
        }

        if (!ModelState.IsValid)
        {
            var sessions = await _context.SessoesConta.Where(s => s.UsuarioId == usuario.Id)
                .OrderByDescending(s => s.UltimaAtividade).ToListAsync();
            ViewBag.Sessoes = sessions;
            ViewBag.SessaoAtual = User.FindFirstValue("AccountSessionId");
            ViewBag.EmailConfirmado = usuario.PerfilConta?.EmailConfirmado ?? false;
            return View("Seguranca", model);
        }

        usuario.SenhaHash = _passwordHasher.HashPassword(usuario, model.NovaSenha);
        var security = await ObterSegurancaAsync(usuario.Id);
        security.SecurityStamp = Guid.NewGuid().ToString("N");
        var sessionId = User.FindFirstValue("AccountSessionId");
        var otherSessions = await _context.SessoesConta
            .Where(s => s.UsuarioId == usuario.Id && s.ChaveSessao != sessionId).ToListAsync();
        _context.SessoesConta.RemoveRange(otherSessions);
        await _context.SaveChangesAsync();

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CriarPrincipal(usuario, security.SecurityStamp, sessionId!),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

        TempData["ContaMensagem"] = "Senha alterada. As outras sessões foram encerradas.";
        return RedirectToAction(nameof(Seguranca));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EncerrarOutrasSessoes()
    {
        var usuarioId = UsuarioIdAtual();
        var sessionId = User.FindFirstValue("AccountSessionId");
        var sessions = await _context.SessoesConta
            .Where(s => s.UsuarioId == usuarioId && s.ChaveSessao != sessionId).ToListAsync();
        _context.SessoesConta.RemoveRange(sessions);
        await _context.SaveChangesAsync();
        TempData["ContaMensagem"] = "As outras sessões foram encerradas.";
        return RedirectToAction(nameof(Seguranca));
    }

    [HttpGet]
    public IActionResult EsqueciSenha() => View(new RecuperarSenhaViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EsqueciSenha(RecuperarSenhaViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = NormalizarEmail(model.Email);
        if (!_emailService.IsAvailable)
        {
            TempData["ErroConta"] = "A recuperação por e-mail está temporariamente indisponível. Tente novamente mais tarde.";
            return RedirectToAction(nameof(EsqueciSenha));
        }

        var usuario = await _context.Usuarios.Include(u => u.PerfilConta)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.Tipo == "Cliente" && u.Status == "Ativo");
        if (usuario != null)
        {
            var rawToken = CriarTokenSeguro();
            await InvalidarTokensAsync(usuario.Id, ResetPasswordPurpose);
            _context.TokensConta.Add(new TokenConta
            {
                UsuarioId = usuario.Id,
                Finalidade = ResetPasswordPurpose,
                TokenHash = HashToken(rawToken),
                ExpiraEm = DateTime.UtcNow.AddMinutes(30)
            });
            await _context.SaveChangesAsync();
            var link = CriarLinkPublico(nameof(RedefinirSenha), new { email, token = rawToken });
            try
            {
                await _emailService.EnviarLinkRedefinicaoAsync(email, link);
            }
            catch (ContaEmailException exception)
            {
                var tokenRecord = await _context.TokensConta.FirstAsync(t =>
                    t.UsuarioId == usuario.Id && t.Finalidade == ResetPasswordPurpose &&
                    t.TokenHash == HashToken(rawToken));
                tokenRecord.Utilizado = true;
                await _context.SaveChangesAsync();
                TempData["ErroConta"] = exception.Message;
                return RedirectToAction(nameof(EsqueciSenha));
            }
        }

        TempData["ContaMensagem"] = "Se houver uma conta ativa para esse e-mail, enviaremos um link temporário de redefinição.";
        return RedirectToAction(nameof(EsqueciSenha));
    }

    [HttpGet]
    public IActionResult RedefinirSenha(string email, string token)
    {
        return View(new RedefinirSenhaViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RedefinirSenha(RedefinirSenhaViewModel model)
    {
        var email = NormalizarEmail(model.Email);
        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == email && u.Tipo == "Cliente");
        var hash = HashToken(model.Token);
        var token = usuario == null ? null : await _context.TokensConta.FirstOrDefaultAsync(t =>
            t.UsuarioId == usuario.Id && t.Finalidade == ResetPasswordPurpose && t.TokenHash == hash &&
            !t.Utilizado && t.ExpiraEm > DateTime.UtcNow);

        if (token == null)
        {
            ModelState.AddModelError(string.Empty, "O link é inválido ou expirou. Solicite uma nova redefinição.");
        }

        if (!ModelState.IsValid || usuario == null || token == null)
        {
            return View(model);
        }

        usuario.SenhaHash = _passwordHasher.HashPassword(usuario, model.NovaSenha);
        token.Utilizado = true;
        var security = await ObterSegurancaAsync(usuario.Id);
        security.SecurityStamp = Guid.NewGuid().ToString("N");
        var sessions = await _context.SessoesConta.Where(s => s.UsuarioId == usuario.Id).ToListAsync();
        _context.SessoesConta.RemoveRange(sessions);
        await _context.SaveChangesAsync();
        TempData["ContaMensagem"] = "Senha redefinida. Entre novamente com a nova senha.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmarEmail(int usuarioId, string token)
    {
        var usuario = await _context.Usuarios.Include(u => u.PerfilConta).FirstOrDefaultAsync(u => u.Id == usuarioId);
        if (usuario?.PerfilConta == null)
        {
            return NotFound();
        }

        var tokenHash = HashToken(token);
        var storedToken = await _context.TokensConta.FirstOrDefaultAsync(t =>
            t.UsuarioId == usuarioId && t.Finalidade == ConfirmEmailPurpose && t.TokenHash == tokenHash &&
            !t.Utilizado && t.ExpiraEm > DateTime.UtcNow);
        if (storedToken == null)
        {
            TempData["ErroConta"] = "O link de confirmação é inválido ou expirou.";
            return RedirectToAction(nameof(Login));
        }

        storedToken.Utilizado = true;
        usuario.PerfilConta.EmailConfirmado = true;
        await _context.SaveChangesAsync();
        TempData["ContaMensagem"] = "E-mail confirmado com sucesso.";
        return RedirectToAction(User.Identity?.IsAuthenticated == true ? nameof(Seguranca) : nameof(Login));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReenviarConfirmacaoEmail()
    {
        var usuario = await UsuarioComPerfilAtualAsync();
        if (usuario == null)
        {
            return Challenge();
        }

        if (!_emailService.IsAvailable)
        {
            TempData["ErroConta"] = "A confirmação por e-mail está temporariamente indisponível.";
            return RedirectToAction(nameof(Seguranca));
        }

        try
        {
            await EnviarConfirmacaoEmailAsync(usuario);
            TempData["ContaMensagem"] = "Enviamos um link para confirmar seu e-mail.";
        }
        catch (ContaEmailException exception)
        {
            TempData["ErroConta"] = exception.Message;
        }
        return RedirectToAction(nameof(Seguranca));
    }

    private async Task EnviarConfirmacaoEmailAsync(Usuario usuario)
    {
        var rawToken = CriarTokenSeguro();
        await InvalidarTokensAsync(usuario.Id, ConfirmEmailPurpose);
        _context.TokensConta.Add(new TokenConta
        {
            UsuarioId = usuario.Id,
            Finalidade = ConfirmEmailPurpose,
            TokenHash = HashToken(rawToken),
            ExpiraEm = DateTime.UtcNow.AddHours(24)
        });
        await _context.SaveChangesAsync();
        var link = CriarLinkPublico(nameof(ConfirmarEmail),
            new { usuarioId = usuario.Id, token = rawToken });
        await _emailService.EnviarLinkConfirmacaoAsync(usuario.Email, link);
    }

    private string CriarLinkPublico(string action, object routeValues)
    {
        var path = Url.Action(action, "Conta", routeValues);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Não foi possível gerar o link da conta.");
        }

        var publicBaseUrl = _configuration["Application:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(publicBaseUrl) && _environment.IsDevelopment())
        {
            publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
        }

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment) ||
            baseUri.AbsolutePath != "/" ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            (!_environment.IsDevelopment() && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new EmailConfigurationException(
                "Configure Application__PublicBaseUrl com a origem HTTPS pública da loja.");
        }

        return $"{baseUri.GetLeftPart(UriPartial.Authority)}{path}";
    }

    private async Task InvalidarTokensAsync(int usuarioId, string finalidade)
    {
        var activeTokens = await _context.TokensConta.Where(t =>
            t.UsuarioId == usuarioId && t.Finalidade == finalidade && !t.Utilizado).ToListAsync();
        foreach (var token in activeTokens)
        {
            token.Utilizado = true;
        }
    }

    private async Task<SegurancaConta> ObterSegurancaAsync(int usuarioId)
    {
        var security = await _context.SegurancasConta.FirstOrDefaultAsync(s => s.UsuarioId == usuarioId);
        if (security != null)
        {
            return security;
        }

        security = new SegurancaConta { UsuarioId = usuarioId };
        _context.SegurancasConta.Add(security);
        return security;
    }

    private async Task<Usuario?> UsuarioComPerfilAtualAsync()
    {
        var usuario = await _context.Usuarios.Include(u => u.PerfilConta)
            .FirstOrDefaultAsync(u => u.Id == UsuarioIdAtual());
        if (usuario != null && usuario.PerfilConta == null)
        {
            usuario.PerfilConta = new PerfilConta
            {
                UsuarioId = usuario.Id,
                Sobrenome = string.Empty,
                TermosAceitosEm = null
            };
            await _context.SaveChangesAsync();
        }

        return usuario;
    }

    private async Task<List<Notificacao>> ObterNotificacoesAsync(int usuarioId) =>
        await _context.Notificacoes.Where(n => n.UsuarioId == usuarioId)
            .OrderByDescending(n => n.CriadaEm).Take(20).ToListAsync();

    private int UsuarioIdAtual() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId) ? usuarioId : 0;

    private static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();

    private static string CriarTokenSeguro() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static ClaimsPrincipal CriarPrincipal(Usuario usuario, string securityStamp, string sessionId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Name, usuario.Nome),
            new Claim(ClaimTypes.Role, usuario.Tipo),
            new Claim("AccountSecurityStamp", securityStamp),
            new Claim("AccountSessionId", sessionId)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
