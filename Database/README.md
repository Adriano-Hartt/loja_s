# Banco de dados Felibow (SQL Server) — PIM IV, Etapa 7

Banco **FelibowLoja** compartilhado pela loja Web (MVC), pela API REST (`/api/v1`) e pelo app Mobile.
As tabelas e colunas seguem o mapeamento do Entity Framework Core do `loja_s`, então a aplicação usa este
banco sem migrations.

| Arquivo | Conteúdo |
|---|---|
| `01_schema.sql` | Criação do banco, 20 tabelas, PK/FK/UNIQUE/CHECK/DEFAULT, índices, função `fn_PrecoEfetivo`, views |
| `02_triggers.sql` | 7 triggers (regras de negócio no banco) |
| `03_procedures.sql` | 9 stored procedures |
| `04_seed.sql` | Categorias, fornecedores (fictícios) e os 8 produtos do catálogo |
| `05_testes.sql` | Testes de triggers/procedures — roda em transação que é sempre desfeita |
| `06_usuario_aplicacao.sql` | Usuário `felibow_app` com privilégio mínimo (senha informada por variável) |
| `felibow_banco_completo.sql` | **Script completo** = 01 + 02 + 03 + 04 (UTF-8 com BOM) |
| `modelagem/` | MER conceitual e modelo lógico (`.mmd` editável + `.png`) |
| `docker-compose.yml` | SQL Server 2022 local já inicializado com os scripts |

## Como executar

**Opção A — Docker (mais simples)**

```bash
cd Database
cp .env.example .env        # troque as duas senhas
docker compose up -d        # sobe o SQL Server e executa os scripts
```

**Opção B — SQL Server já instalado (SSMS ou sqlcmd)**

```bash
sqlcmd -S localhost -U sa -P "<senha>" -C -f 65001 -b -i felibow_banco_completo.sql
sqlcmd -S localhost -U sa -P "<senha>" -C -f 65001 -b -i 05_testes.sql
```

Os arquivos são UTF-8; no `sqlcmd` use `-f 65001` para preservar acentos. Todos os scripts são idempotentes.

## Ligando a loja e a API ao SQL Server

No `loja_s`, defina (variáveis de ambiente, *user-secrets* ou `appsettings.Development.json` fora do Git):

```text
Database__Provider=SqlServer
ConnectionStrings__SqlServer=Server=localhost,1433;Database=FelibowLoja;User Id=felibow_app;Password=<APP_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=True
Jwt__Key=<texto aleatório com 48+ caracteres>
```

Sem `Database__Provider`, a aplicação continua usando SQLite (desenvolvimento rápido, sem triggers/procedures).
Os endpoints que dependem de procedures (cancelar pedido, resumo da conta e área administrativa) respondem
`501` em SQLite.

Para criar um administrador: cadastre-se normalmente e execute `EXEC dbo.sp_PromoverAdministrador N'seu@email';`.
Obs.: o login do **site MVC** só aceita contas `Cliente`; o administrador usa a **API** (`/api/v1/auth/login`).

## Dicionário resumido

| Tabela | Finalidade | PK | Chaves estrangeiras |
|---|---|---|---|
| Usuarios | Clientes e administradores | Id | — |
| PerfisConta | Dados complementares e preferências de notificação | UsuarioId | Usuarios |
| SegurancasConta | Tentativas, bloqueio e *security stamp* | UsuarioId | Usuarios |
| SessoesConta | Sessões/dispositivos (revogáveis) | Id | Usuarios |
| TokensConta | Tokens de redefinição/confirmação (hash) | Id | Usuarios |
| MetodosPagamentoSalvos | Cartão tokenizado (bandeira + 4 dígitos) | Id | Usuarios |
| SolicitacoesSuporte | Central de atendimento | Id | Usuarios |
| Notificacoes | Avisos ao usuário (inclui os gerados por triggers) | Id | Usuarios |
| Categorias / Fornecedores | Classificação e origem do produto | Id | — |
| Produtos | Catálogo e estoque | Id | Categorias, Fornecedores |
| Enderecos | Endereços de entrega | Id | Usuarios |
| Carrinhos / ItensCarrinho | Carrinho persistido da conta | Id | Usuarios; Carrinhos, Produtos |
| Favoritos | Produtos favoritos | Id | Usuarios, Produtos |
| Pedidos / ItensPedido | Pedido e linhas (preço congelado) | Id | Usuarios, Enderecos; Pedidos, Produtos |
| Pagamentos | Pagamento 1:1 com o pedido | Id | Pedidos |
| HistoricoPagamentos | Trilha de mudanças do status de pagamento | Id | Pedidos |
| MovimentacoesEstoque | Entradas, saídas e ajustes | Id | Produtos, Usuarios |

### Triggers

| Trigger | Tabela | O que garante |
|---|---|---|
| `trg_Produtos_AtualizaData` | Produtos | Carimba `DataAtualizacao` |
| `trg_Produtos_AlertaEstoqueMinimo` | Produtos | Notifica administradores quando o estoque cruza o mínimo |
| `trg_Pagamentos_SincronizaPedido` | Pagamentos | Pagamento → `StatusPagamento` do pedido; pago finaliza o pedido |
| `trg_Pedidos_HistoricoPagamento` | Pedidos | Grava `HistoricoPagamentos` a cada mudança |
| `trg_Pedidos_ControlaStatus` | Pedidos | Valida transições; no cancelamento devolve estoque, registra movimentação e estorna o pagamento |
| `trg_Pedidos_NotificaCliente` | Pedidos | Notifica envio, entrega e cancelamento |
| `trg_ItensPedido_Imutavel` | ItensPedido | Impede alterar itens de pedido já processado |

Fluxo de status do pedido: `Pendente → AguardandoPagamento → Finalizado → Enviado → Entregue`; cancelamento é permitido
até `Finalizado`.

### Procedures

`sp_AtualizarStatusPedido`, `sp_CancelarPedidoCliente`, `sp_AjustarEstoque`, `sp_ProdutosEstoqueBaixo`,
`sp_RelatorioVendasPorPeriodo`, `sp_ProdutosMaisVendidos`, `sp_ObterResumoCliente` (usadas pela API) e
`sp_PromoverAdministrador`, `sp_LimparDadosExpirados` (operação/manutenção, agendável no SQL Server Agent).

## Limitações conhecidas

- O painel `bow.estoque` continua com SQLite próprio; ainda não foi migrado para este banco.
- Scripts validados apenas por análise sintática (parser T-SQL). **Execute `05_testes.sql` no seu SQL Server**
  para confirmar o comportamento real.
