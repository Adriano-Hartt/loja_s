using loja_s.Api;
using loja_s.Data;
using loja_s.Models;
using loja_s.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Provedor de banco: "Sqlite" (padrão, desenvolvimento) ou "SqlServer" (PIM IV: triggers e procedures).
var useSqlServer = string.Equals(
    builder.Configuration["Database:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase);

string shopConnectionString;
if (useSqlServer)
{
    shopConnectionString = builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException(
            "Database:Provider=SqlServer exige a cadeia de conexão ConnectionStrings:SqlServer (veja Database/README.md).");
}
else
{
    shopConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=felibow_integrated.db";
    shopConnectionString = Felibow.Integration.SqliteConnectionStringResolver.Resolve(
        shopConnectionString, builder.Environment.ContentRootPath);
}

// Segredo do JWT: nunca fica no código. Em Development, se não houver configuração, usa uma chave
// temporária (os tokens deixam de valer quando o servidor reinicia).
var jwtOptions = new JwtOptions
{
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "felibow-loja",
    Audience = builder.Configuration["Jwt:Audience"] ?? "felibow-clientes",
    Key = builder.Configuration["Jwt:Key"] ?? string.Empty,
    ExpiracaoHoras = builder.Configuration.GetValue("Jwt:ExpiracaoHoras", 8)
};
var jwtKeyTemporaria = false;
if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Configure Jwt:Key (variável de ambiente Jwt__Key) com um segredo aleatório de pelo menos 32 caracteres.");
    }

    jwtOptions.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    jwtKeyTemporaria = true;
}

if (jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key deve ter pelo menos 32 caracteres.");
}

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // Afeta apenas respostas JSON da API (as views MVC não serializam dicionários).
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.Name = "felibow.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (useSqlServer)
    {
        options.UseSqlServer(shopConnectionString);
    }
    else
    {
        options.UseSqlite(shopConnectionString);
    }
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Conta/Login";
        options.AccessDeniedPath = "/Conta/Login";
        options.Cookie.Name = "felibow.customer";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnValidatePrincipal = async context =>
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = context.Principal?.FindFirstValue("AccountSecurityStamp");
            var sessionId = context.Principal?.FindFirstValue("AccountSessionId");
            if (!int.TryParse(userIdValue, out var userId) ||
                string.IsNullOrWhiteSpace(stamp) ||
                string.IsNullOrWhiteSpace(sessionId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var accountSession = await db.SessoesConta
                .Include(s => s.Usuario)
                .ThenInclude(u => u.SegurancaConta)
                .FirstOrDefaultAsync(s => s.UsuarioId == userId && s.ChaveSessao == sessionId);
            if (accountSession?.Usuario.Status != "Ativo" ||
                accountSession.Usuario.SegurancaConta?.SecurityStamp != stamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var now = DateTime.UtcNow;
            if (accountSession.UltimaAtividade <= now.AddMinutes(-5))
            {
                accountSession.UltimaAtividade = now;
                await db.SaveChangesAsync();
            }
        };
    })
    // Esquema usado pela API REST (/api/v1/*) e pelo aplicativo mobile: JWT + sessão revogável.
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            // Mesma verificação do cookie: sessão existente, usuário ativo, security stamp igual e papel atual.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var userIdValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var stamp = principal?.FindFirstValue("AccountSecurityStamp");
                var sessionId = principal?.FindFirstValue("AccountSessionId");
                var role = principal?.FindFirstValue(ClaimTypes.Role);
                if (!int.TryParse(userIdValue, out var userId) ||
                    string.IsNullOrWhiteSpace(stamp) ||
                    string.IsNullOrWhiteSpace(sessionId))
                {
                    context.Fail("Token sem as informações de sessão.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                var accountSession = await db.SessoesConta
                    .Include(s => s.Usuario)
                    .ThenInclude(u => u.SegurancaConta)
                    .FirstOrDefaultAsync(s => s.UsuarioId == userId && s.ChaveSessao == sessionId);
                if (accountSession?.Usuario.Status != "Ativo" ||
                    accountSession.Usuario.SegurancaConta?.SecurityStamp != stamp ||
                    !string.Equals(accountSession.Usuario.Tipo, role, StringComparison.Ordinal))
                {
                    context.Fail("Sessão encerrada ou inválida.");
                    return;
                }

                var now = DateTime.UtcNow;
                if (accountSession.UltimaAtividade <= now.AddMinutes(-5))
                {
                    accountSession.UltimaAtividade = now;
                    await db.SaveChangesAsync();
                }
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Protege login e cadastro contra tentativas em massa (por IP).
    options.AddPolicy<string>("auth", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<IContaEmailService, ContaEmailService>();
builder.Services.AddScoped<EstoqueService>();
builder.Services.AddScoped<IPagamentoService, PagamentoSimuladoService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<CarrinhoService>(services =>
    new CarrinhoService(
        services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Session,
        services.GetRequiredService<ApplicationDbContext>()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Felibow API",
        Version = "v1",
        Description = "API REST da loja Felibow, consumida pelo aplicativo mobile. " +
                      "Faça login em /api/v1/auth/login e use o botão Authorize com o accessToken."
    });
    options.DocInclusionPredicate((_, api) =>
        api.RelativePath?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true);
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe somente o token JWT retornado por /api/v1/auth/login."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (jwtKeyTemporaria)
{
    app.Logger.LogWarning(
        "Jwt:Key não configurada: usando chave temporária de desenvolvimento. " +
        "Configure Jwt__Key (ou user-secrets) para manter os tokens válidos entre reinicializações.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (useSqlServer)
    {
        // O esquema vem do script Database/felibow_banco_completo.sql (tabelas, triggers e procedures).
        await EnsureSqlServerDatabaseAsync(db);
    }
    else
    {
        await EnsureShopSchemaAsync(db);
        await EnsureProductsTableAsync(db);
        await ContaSchema.EnsureCreatedAsync(db);
        await Felibow.Integration.IntegratedDatabaseImporter.ImportAvailableSourcesAsync(
            db,
            Path.Combine(builder.Environment.ContentRootPath, "loja_s.db"),
            Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "bow.estoque", "bow_estoque.db")),
            app.Logger);
    }

    SeedProdutos(db);

    // Promove uma conta já cadastrada a Admin (ex.: Admin__Email=voce@email.com). Em SQL Server também
    // existe a procedure dbo.sp_PromoverAdministrador.
    var adminEmail = builder.Configuration["Admin:Email"]?.Trim().ToLowerInvariant();
    if (!string.IsNullOrEmpty(adminEmail))
    {
        var conta = db.Usuarios.FirstOrDefault(u => u.Email.ToLower() == adminEmail);
        if (conta != null && conta.Tipo != "Admin")
        {
            conta.Tipo = "Admin";
            db.SaveChanges();
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    // Site MVC: página de erro HTML. API (/api): resposta JSON no formato ProblemDetails.
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseExceptionHandler("/Home/Error"));
    app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseExceptionHandler(errorApp => errorApp.Run(WriteApiErrorAsync)));
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Felibow API v1"));
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static async Task WriteApiErrorAsync(HttpContext context)
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno do servidor.",
            Detail = "Não foi possível concluir a operação. Tente novamente em instantes."
        },
        (JsonSerializerOptions?)null,
        "application/problem+json");
}

static async Task EnsureSqlServerDatabaseAsync(ApplicationDbContext context)
{
    if (!await context.Database.CanConnectAsync())
    {
        throw new InvalidOperationException(
            "Não foi possível conectar ao SQL Server. Confira ConnectionStrings:SqlServer e se o banco " +
            "FelibowLoja foi criado com Database/felibow_banco_completo.sql.");
    }

    var productsTableExists = await context.Database.SqlQueryRaw<int>(
        "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Produtos'")
        .SingleAsync();
    if (productsTableExists == 0)
    {
        throw new InvalidOperationException(
            "O banco conectado não possui a tabela Produtos. Execute Database/felibow_banco_completo.sql.");
    }
}

static async Task EnsureShopSchemaAsync(ApplicationDbContext context)
{
    var createScript = context.Database.GenerateCreateScript()
        .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
        .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase)
        .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase);
    await context.Database.ExecuteSqlRawAsync(createScript);
}

static async Task EnsureProductsTableAsync(ApplicationDbContext context)
{
    var productsTableExists = await context.Database.SqlQueryRaw<int>(
        """SELECT COUNT(*) AS "Value" FROM sqlite_master WHERE type = 'table' AND name = 'Produtos'""")
        .SingleAsync();
    if (productsTableExists == 0)
    {
        throw new InvalidOperationException(
            $"The SQLite database '{context.Database.GetDbConnection().DataSource}' is missing the required Produtos table after schema initialization.");
    }
}

static void SeedProdutos(ApplicationDbContext context)
{
    // Imagens em wwwroot/img/produtos com nomes que dizem o produto (creatina.jpg, whey.jpg, ...).
    var produtosIniciais = new[]
    {
        new Produto { Nome = "VitaPrime — Creatina Monohidratada 300 g", Preco = 149.90m, Descricao = "Creatina monohidratada pura, em pó, sem sabor. Suplemento alimentar para quem treina e busca apoio para força e desempenho. Fácil de misturar em água ou suco.", ImagemUrl = "~/img/produtos/creatina.png", SKU = "PD-001", Estoque = 24, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "WheyCore — Whey Protein Chocolate 900 g", Preco = 169.90m, Descricao = "Proteína do soro do leite com sabor chocolate. Suplemento alimentar para complementar a ingestão de proteínas na rotina de treino. Prático para o pós-treino.", ImagemUrl = "~/img/produtos/whey.png", SKU = "DW-002", Estoque = 18, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "MultiVida — Multivitamínico 60 cápsulas", Preco = 99.90m, Descricao = "Combinação de vitaminas e minerais para complementar a alimentação. Suplemento alimentar para a rotina do dia a dia.", ImagemUrl = "~/img/produtos/multivitaminico.png", SKU = "CE-003", Estoque = 20, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "OceanPure — Ômega 3 120 cápsulas", Preco = 129.90m, Descricao = "Óleo de peixe com EPA e DHA em cápsulas. Suplemento alimentar para complementar a dieta com ácidos graxos ômega 3.", ImagemUrl = "~/img/produtos/omega3.png", SKU = "BS-004", Estoque = 14, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "MagnaBalance — Magnésio 60 cápsulas", Preco = 89.90m, Descricao = "Suplemento alimentar de magnésio para complementar a dieta. Indicado para quem busca equilíbrio nutricional na rotina.", ImagemUrl = "~/img/produtos/magnesio.png", SKU = "MB-005", Estoque = 16, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "SolVita — Vitamina D3 + K2 60 cápsulas", Preco = 79.90m, Descricao = "Associação das vitaminas D3 e K2 em cápsulas. Suplemento alimentar para complementar a alimentação.", ImagemUrl = "~/img/produtos/d3k2.png", SKU = "SP-006", Estoque = 18, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "GlutaFlow — Glutamina 300 g", Preco = 109.90m, Descricao = "Glutamina em pó para quem treina e quer complementar a nutrição esportiva. Pode ser misturada em água ou shakes.", ImagemUrl = "~/img/produtos/glutamina.png", SKU = "MR-007", Estoque = 12, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "DuoPower — Pré-treino Frutas Vermelhas", Preco = 94.90m, Descricao = "Suplemento alimentar em pó com cafeína, beta-alanina e taurina, no sabor frutas vermelhas. Para quem busca um complemento para a rotina de treino.", ImagemUrl = "~/img/produtos/duopower.png", SKU = "DP-009", Estoque = 15, Status = "Ativo", EdicaoEspecial = null },
        new Produto { Nome = "TurboVolt — Pré-treino Blueberry", Preco = 99.90m, Descricao = "Suplemento alimentar em pó com cafeína, creatina e beta-alanina, no sabor blueberry. Para quem busca apoio na rotina de treino.", ImagemUrl = "~/img/produtos/turbovolt.png", SKU = "TV-010", Estoque = 15, Status = "Ativo", EdicaoEspecial = null }
    };

    var existentes = context.Produtos.ToList();
    foreach (var novo in produtosIniciais)
    {
        var atual = existentes.FirstOrDefault(p => string.Equals(p.SKU, novo.SKU, StringComparison.OrdinalIgnoreCase));
        if (atual == null)
        {
            context.Produtos.Add(novo);
            continue;
        }

        // Atualiza nome, texto e imagem dos produtos já gravados (preço e estoque ficam como estão).
        atual.Nome = novo.Nome;
        atual.Descricao = novo.Descricao;
        atual.ImagemUrl = novo.ImagemUrl;
        atual.EdicaoEspecial = null;
    }

    // Produto antigo sem imagem e sem equivalente na nova linha: sai do catálogo (não é apagado, para não quebrar pedidos).
    var antigo = existentes.FirstOrDefault(p => string.Equals(p.SKU, "MGB-008", StringComparison.OrdinalIgnoreCase));
    if (antigo != null)
    {
        antigo.Status = "Inativo";
    }

    context.SaveChanges();
}
