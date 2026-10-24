using loja_s.Data;
using loja_s.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace loja_s.Api;

/// <summary>Cadastro, login (JWT), logout e dados do usuário autenticado.</summary>
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthApiController : ApiControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<Usuario> _hasher;
    private readonly IJwtTokenService _jwt;

    public AuthApiController(ApplicationDbContext db, IPasswordHasher<Usuario> hasher, IJwtTokenService jwt)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    /// <summary>Cria uma conta de cliente (mesmas regras do cadastro do site).</summary>
    [HttpPost("registrar")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Registrar([FromBody] RegistrarRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Usuarios.AnyAsync(u => u.Email.ToLower() == email))
        {
            return Falha(StatusCodes.Status409Conflict, "E-mail já cadastrado.", "Este e-mail já está cadastrado.");
        }

        var usuario = new Usuario
        {
            Nome = request.Nome.Trim(),
            Email = email,
            Telefone = request.Telefone?.Trim(),
            CPF = string.Empty,
            Tipo = "Cliente",
            Status = "Ativo",
            DataCadastro = DateTime.UtcNow
        };

        await using var transaction = await _db.Database.BeginTransactionAsync();
        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();

        usuario.SenhaHash = _hasher.HashPassword(usuario, request.Senha);
        usuario.PerfilConta = new PerfilConta
        {
            UsuarioId = usuario.Id,
            Sobrenome = request.Sobrenome.Trim(),
            DataNascimento = request.DataNascimento,
            TermosAceitosEm = DateTime.UtcNow
        };
        usuario.SegurancaConta = new SegurancaConta { UsuarioId = usuario.Id };
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return StatusCode(StatusCodes.Status201Created, usuario.ParaDto());
    }

    /// <summary>Autentica e devolve um JWT atrelado a uma sessão revogável do aparelho.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        const string mensagemGenerica = "E-mail ou senha inválidos.";
        var email = request.Email.Trim().ToLowerInvariant();

        var usuario = await _db.Usuarios
            .Include(u => u.SegurancaConta)
            .Include(u => u.PerfilConta)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && (u.Tipo == "Cliente" || u.Tipo == "Admin"));

        if (usuario == null || usuario.Status != "Ativo")
        {
            return Falha(StatusCodes.Status401Unauthorized, "Credenciais inválidas.", mensagemGenerica);
        }

        var seguranca = usuario.SegurancaConta;
        if (seguranca == null)
        {
            seguranca = new SegurancaConta { UsuarioId = usuario.Id };
            _db.SegurancasConta.Add(seguranca);
        }

        if (seguranca.BloqueadoAte > DateTime.UtcNow)
        {
            return Falha(
                StatusCodes.Status423Locked,
                "Acesso temporariamente bloqueado.",
                "Muitas tentativas incorretas. Aguarde alguns minutos e tente novamente.");
        }

        var resultado = _hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, request.Senha);
        if (resultado == PasswordVerificationResult.Failed)
        {
            seguranca.TentativasFalhas++;
            if (seguranca.TentativasFalhas >= 5)
            {
                seguranca.TentativasFalhas = 0;
                seguranca.BloqueadoAte = DateTime.UtcNow.AddMinutes(15);
            }

            await _db.SaveChangesAsync();
            return Falha(StatusCodes.Status401Unauthorized, "Credenciais inválidas.", mensagemGenerica);
        }

        seguranca.TentativasFalhas = 0;
        seguranca.BloqueadoAte = null;
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            usuario.SenhaHash = _hasher.HashPassword(usuario, request.Senha);
        }

        var agora = DateTime.UtcNow;
        var inativas = await _db.SessoesConta
            .Where(s => s.UsuarioId == usuario.Id && s.UltimaAtividade < agora.AddDays(-30))
            .ToListAsync();
        _db.SessoesConta.RemoveRange(inativas);

        var chaveSessao = Guid.NewGuid().ToString("N");
        var dispositivo = string.IsNullOrWhiteSpace(request.Dispositivo)
            ? Request.Headers.UserAgent.ToString().Trim()
            : request.Dispositivo.Trim();
        if (dispositivo.Length > 250)
        {
            dispositivo = dispositivo[..250];
        }

        _db.SessoesConta.Add(new SessaoConta
        {
            UsuarioId = usuario.Id,
            ChaveSessao = chaveSessao,
            Dispositivo = dispositivo,
            CriadaEm = agora,
            UltimaAtividade = agora
        });
        await _db.SaveChangesAsync();

        var (token, expira) = _jwt.Criar(usuario, chaveSessao, seguranca.SecurityStamp);
        return Ok(new LoginResponse(token, "Bearer", ApiMapper.Utc(expira), usuario.ParaDto()));
    }

    /// <summary>Encerra a sessão deste aparelho (o token deixa de valer imediatamente).</summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        var chave = User.FindFirstValue("AccountSessionId");
        var usuarioId = UsuarioIdAtual;
        if (!string.IsNullOrWhiteSpace(chave))
        {
            var sessao = await _db.SessoesConta
                .FirstOrDefaultAsync(s => s.UsuarioId == usuarioId && s.ChaveSessao == chave);
            if (sessao != null)
            {
                _db.SessoesConta.Remove(sessao);
                await _db.SaveChangesAsync();
            }
        }

        return NoContent();
    }

    /// <summary>Dados do usuário autenticado (inclui CPF e telefone para pré-preencher o checkout).</summary>
    [HttpGet("eu")]
    [Authorize(AuthenticationSchemes = Regras.EsquemaJwt)]
    [ProducesResponseType(typeof(UsuarioDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Eu()
    {
        var usuarioId = UsuarioIdAtual;
        var usuario = await _db.Usuarios.AsNoTracking()
            .Include(u => u.PerfilConta)
            .FirstOrDefaultAsync(u => u.Id == usuarioId);
        return usuario == null ? Unauthorized() : Ok(usuario.ParaDto());
    }
}
