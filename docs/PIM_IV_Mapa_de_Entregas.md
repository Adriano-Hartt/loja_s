# PIM IV – Mapa entre exigências do manual e entregas

| Exigência (Manual PIM IV – ADS) | Entrega |
|---|---|
| Etapa 4 – APIs REST, autenticação, segurança, estrutura administrativa | `loja_s/Api/*` (`/api/v1`), JWT + sessão revogável, rate limiting, ProblemDetails, Swagger (Development em `/swagger`), `AdminApiController` (perfil Admin) |
| Etapa 4 – integração entre módulos | API reutiliza `PedidoService`/`CarrinhoService` do site; mesmo banco para Web, API e Mobile |
| Etapa 5 – Mobile: telas, navegação, autenticação, sincronização, integração com API | `Mobile/Felibow.Mobile` (MAUI) – ver `Mobile/README.md` |
| Etapa 7 – modelo lógico, físico, MER | `Database/modelagem/*` (.mmd e .png), `Database/01_schema.sql` |
| Etapa 7 – procedures e triggers | `Database/03_procedures.sql` (9), `Database/02_triggers.sql` (7) |
| Etapa 7 – script completo | `Database/felibow_banco_completo.sql` (+ seed, testes `05_testes.sql`, usuário `06`) |
| Banco usado por API/Web/Mobile | `Database:Provider=SqlServer` + `ConnectionStrings:SqlServer`; Mobile só acessa via API |

## Painel administrativo (site MVC)
Entre em `/Conta/Login` com uma conta de perfil Admin (promova com `Admin__Email=seu@email` ao iniciar, ou `EXEC dbo.sp_PromoverAdministrador`). Telas em `/Admin`: painel, pedidos (filtro, detalhe, troca de status), produtos (preço, promoção, estoque mínimo, ajuste de estoque) e relatórios (vendas por dia, mais vendidos).

## Como rodar com SQL Server
```
cd Database && cp .env.example .env && docker compose up -d
cd ../loja_s
export Database__Provider=SqlServer
export ConnectionStrings__SqlServer="Server=localhost,1433;Database=FelibowLoja;User Id=felibow_app;Password=...;TrustServerCertificate=True"
export Jwt__Key="<segredo aleatório de 32+ caracteres>"
dotnet run --launch-profile http
```
Swagger: http://localhost:5258/swagger (Development).

## Roteiro de teste
1. `Database/05_testes.sql` (T1–T7, sempre faz rollback).
2. POST `/api/v1/auth/registrar` → `/auth/login` → autorizar no Swagger com o token.
3. Carrinho → `POST /pedidos` → `POST /pedidos/{id}/cancelar` (estoque devolvido por trigger).
4. Admin: `sp_PromoverAdministrador` e use `/api/v1/admin/*`.
5. App: login, comprar, desligar a API e reabrir o catálogo (modo offline).

## Limitações conhecidas
- Nada foi compilado nem executado no ambiente de geração (sem .NET/SQL Server): rode `dotnet build` e os scripts SQL antes de entregar.
- Endpoints que dependem de procedures retornam 501 com SQLite.
- Pagamento é simulado; o login MVC aceita só Cliente (Admin usa a API).
- Etapas 8 e 9 (Cloud/DevOps, Ágil) não fazem parte desta entrega.
