/* Felibow - script COMPLETO do banco (gerado de 01..04). Execute com sqlcmd -f 65001 ou SSMS. */

/* ############ 01_schema.sql ############ */
/* =====================================================================
   Felibow - Banco de dados da loja (SQL Server 2017+)
   PIM IV - Etapa 7 - Projeto do Banco de Dados
   Arquivo 01: criacao do banco, tabelas, restricoes, indices, funcao e views.

   - Os nomes de tabelas e colunas seguem exatamente o mapeamento do
     Entity Framework Core usado pela aplicacao (loja_s / ApplicationDbContext),
     para que o MESMO banco atenda Web (MVC), API REST e aplicativo Mobile.
   - O script e idempotente: pode ser executado mais de uma vez.
   ===================================================================== */

SET NOCOUNT ON;
GO

IF DB_ID(N'FelibowLoja') IS NULL
BEGIN
    CREATE DATABASE FelibowLoja;
END
GO

USE FelibowLoja;
GO

/* ------------------------------------------------------------------ *
 *  Tabelas de apoio ao catalogo
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Categorias', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categorias
    (
        Id         INT IDENTITY(1,1) NOT NULL,
        Nome       NVARCHAR(100)     NOT NULL,
        Descricao  NVARCHAR(300)     NULL,
        Ativa      BIT               NOT NULL CONSTRAINT DF_Categorias_Ativa DEFAULT (1),
        CONSTRAINT PK_Categorias PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Categorias_Nome UNIQUE (Nome)
    );
END
GO

IF OBJECT_ID(N'dbo.Fornecedores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Fornecedores
    (
        Id           INT IDENTITY(1,1) NOT NULL,
        RazaoSocial  NVARCHAR(150)     NOT NULL,
        CNPJ         NVARCHAR(18)      NOT NULL,
        Email        NVARCHAR(254)     NULL,
        Telefone     NVARCHAR(30)      NULL,
        Ativo        BIT               NOT NULL CONSTRAINT DF_Fornecedores_Ativo DEFAULT (1),
        CONSTRAINT PK_Fornecedores PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Fornecedores_CNPJ UNIQUE (CNPJ)
    );
END
GO

/* ------------------------------------------------------------------ *
 *  Usuarios e dados de conta
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Usuarios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Usuarios
    (
        Id            INT IDENTITY(1,1) NOT NULL,
        Nome          NVARCHAR(100)     NOT NULL,
        Email         NVARCHAR(254)     NOT NULL,
        Telefone      NVARCHAR(30)      NULL,
        CPF           NVARCHAR(14)      NOT NULL CONSTRAINT DF_Usuarios_CPF DEFAULT (N''),
        SenhaHash     NVARCHAR(500)     NOT NULL CONSTRAINT DF_Usuarios_SenhaHash DEFAULT (N''),
        Tipo          NVARCHAR(20)      NOT NULL CONSTRAINT DF_Usuarios_Tipo DEFAULT (N'Cliente'),
        Status        NVARCHAR(20)      NOT NULL CONSTRAINT DF_Usuarios_Status DEFAULT (N'Ativo'),
        DataCadastro  DATETIME2         NOT NULL CONSTRAINT DF_Usuarios_DataCadastro DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Usuarios PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Usuarios_Email UNIQUE (Email),
        CONSTRAINT CK_Usuarios_Tipo   CHECK (Tipo   IN (N'Cliente', N'Admin')),
        CONSTRAINT CK_Usuarios_Status CHECK (Status IN (N'Ativo', N'Inativo', N'Bloqueado'))
    );
END
GO

IF OBJECT_ID(N'dbo.PerfisConta', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerfisConta
    (
        UsuarioId            INT           NOT NULL,
        Sobrenome            NVARCHAR(100) NOT NULL CONSTRAINT DF_PerfisConta_Sobrenome DEFAULT (N''),
        DataNascimento       DATETIME2     NULL,
        TermosAceitosEm      DATETIME2     NULL,
        EmailConfirmado      BIT           NOT NULL CONSTRAINT DF_PerfisConta_EmailConfirmado DEFAULT (0),
        AtualizacoesPedidos  BIT           NOT NULL CONSTRAINT DF_PerfisConta_AtualizacoesPedidos DEFAULT (1),
        Promocoes            BIT           NOT NULL CONSTRAINT DF_PerfisConta_Promocoes DEFAULT (0),
        Novidades            BIT           NOT NULL CONSTRAINT DF_PerfisConta_Novidades DEFAULT (0),
        ProdutosFavoritos    BIT           NOT NULL CONSTRAINT DF_PerfisConta_ProdutosFavoritos DEFAULT (1),
        AlertasSeguranca     BIT           NOT NULL CONSTRAINT DF_PerfisConta_AlertasSeguranca DEFAULT (1),
        CONSTRAINT PK_PerfisConta PRIMARY KEY CLUSTERED (UsuarioId),
        CONSTRAINT FK_PerfisConta_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.SegurancasConta', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SegurancasConta
    (
        UsuarioId         INT           NOT NULL,
        TentativasFalhas  INT           NOT NULL CONSTRAINT DF_SegurancasConta_Tentativas DEFAULT (0),
        BloqueadoAte      DATETIME2     NULL,
        SecurityStamp     NVARCHAR(64)  NOT NULL,
        CONSTRAINT PK_SegurancasConta PRIMARY KEY CLUSTERED (UsuarioId),
        CONSTRAINT FK_SegurancasConta_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        CONSTRAINT CK_SegurancasConta_Tentativas CHECK (TentativasFalhas >= 0)
    );
END
GO

IF OBJECT_ID(N'dbo.SessoesConta', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessoesConta
    (
        Id               INT IDENTITY(1,1) NOT NULL,
        UsuarioId        INT               NOT NULL,
        ChaveSessao      NVARCHAR(64)      NOT NULL,
        Dispositivo      NVARCHAR(250)     NOT NULL CONSTRAINT DF_SessoesConta_Dispositivo DEFAULT (N''),
        CriadaEm         DATETIME2         NOT NULL CONSTRAINT DF_SessoesConta_CriadaEm DEFAULT (SYSUTCDATETIME()),
        UltimaAtividade  DATETIME2         NOT NULL CONSTRAINT DF_SessoesConta_UltimaAtividade DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_SessoesConta PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_SessoesConta_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_SessoesConta_ChaveSessao UNIQUE (ChaveSessao),
        INDEX IX_SessoesConta_UsuarioId NONCLUSTERED (UsuarioId)
    );
END
GO

IF OBJECT_ID(N'dbo.TokensConta', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TokensConta
    (
        Id          INT IDENTITY(1,1) NOT NULL,
        UsuarioId   INT               NOT NULL,
        Finalidade  NVARCHAR(40)      NOT NULL,
        TokenHash   NVARCHAR(128)     NOT NULL,
        ExpiraEm    DATETIME2         NOT NULL,
        Utilizado   BIT               NOT NULL CONSTRAINT DF_TokensConta_Utilizado DEFAULT (0),
        CONSTRAINT PK_TokensConta PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_TokensConta_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_TokensConta_Hash_Finalidade UNIQUE (TokenHash, Finalidade)
    );
END
GO

IF OBJECT_ID(N'dbo.MetodosPagamentoSalvos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MetodosPagamentoSalvos
    (
        Id             INT IDENTITY(1,1) NOT NULL,
        UsuarioId      INT               NOT NULL,
        Bandeira       NVARCHAR(30)      NOT NULL,
        UltimosQuatro  NVARCHAR(4)       NOT NULL,
        TokenProvedor  NVARCHAR(200)     NOT NULL,
        Preferencial   BIT               NOT NULL CONSTRAINT DF_MetodosPagamentoSalvos_Pref DEFAULT (0),
        DataCadastro   DATETIME2         NOT NULL CONSTRAINT DF_MetodosPagamentoSalvos_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MetodosPagamentoSalvos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_MetodosPagamentoSalvos_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        INDEX IX_MetodosPagamentoSalvos_UsuarioId NONCLUSTERED (UsuarioId)
    );
END
GO

IF OBJECT_ID(N'dbo.SolicitacoesSuporte', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitacoesSuporte
    (
        Id           INT IDENTITY(1,1) NOT NULL,
        UsuarioId    INT               NOT NULL,
        Categoria    NVARCHAR(60)      NOT NULL,
        Assunto      NVARCHAR(150)     NOT NULL,
        Mensagem     NVARCHAR(2000)    NOT NULL,
        Status       NVARCHAR(20)      NOT NULL CONSTRAINT DF_SolicitacoesSuporte_Status DEFAULT (N'Aberta'),
        DataCriacao  DATETIME2         NOT NULL CONSTRAINT DF_SolicitacoesSuporte_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_SolicitacoesSuporte PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_SolicitacoesSuporte_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'dbo.Notificacoes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Notificacoes
    (
        Id         INT IDENTITY(1,1) NOT NULL,
        UsuarioId  INT               NOT NULL,
        Categoria  NVARCHAR(60)      NOT NULL,
        Titulo     NVARCHAR(150)     NOT NULL,
        Mensagem   NVARCHAR(1000)    NOT NULL,
        Url        NVARCHAR(300)     NULL,
        CriadaEm   DATETIME2         NOT NULL CONSTRAINT DF_Notificacoes_CriadaEm DEFAULT (SYSUTCDATETIME()),
        LidaEm     DATETIME2         NULL,
        CONSTRAINT PK_Notificacoes PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Notificacoes_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        INDEX IX_Notificacoes_UsuarioId_CriadaEm NONCLUSTERED (UsuarioId, CriadaEm DESC)
    );
END
GO

/* ------------------------------------------------------------------ *
 *  Produtos
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Produtos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Produtos
    (
        Id                 INT IDENTITY(1,1) NOT NULL,
        Nome               NVARCHAR(200)     NOT NULL,
        SKU                NVARCHAR(50)      NOT NULL,
        PrecoVenda         DECIMAL(18,2)     NOT NULL,
        PrecoPromocional   DECIMAL(18,2)     NULL,
        PrecoCusto         DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Produtos_PrecoCusto DEFAULT (0),
        QuantidadeEstoque  INT               NOT NULL CONSTRAINT DF_Produtos_Estoque DEFAULT (0),
        EstoqueMinimo      INT               NOT NULL CONSTRAINT DF_Produtos_EstoqueMinimo DEFAULT (0),
        EstoqueMaximo      INT               NOT NULL CONSTRAINT DF_Produtos_EstoqueMaximo DEFAULT (0),
        CategoriaId        INT               NULL,
        FornecedorId       INT               NULL,
        CodigoBarras       NVARCHAR(50)      NOT NULL CONSTRAINT DF_Produtos_CodigoBarras DEFAULT (N''),
        Marca              NVARCHAR(100)     NOT NULL CONSTRAINT DF_Produtos_Marca DEFAULT (N''),
        EdicaoEspecial     NVARCHAR(100)     NULL,
        Descricao          NVARCHAR(1000)    NOT NULL CONSTRAINT DF_Produtos_Descricao DEFAULT (N''),
        Imagem             NVARCHAR(300)     NOT NULL CONSTRAINT DF_Produtos_Imagem DEFAULT (N''),
        Status             NVARCHAR(20)      NOT NULL CONSTRAINT DF_Produtos_Status DEFAULT (N'Ativo'),
        DataCadastro       DATETIME2         NOT NULL CONSTRAINT DF_Produtos_DataCadastro DEFAULT (SYSUTCDATETIME()),
        DataAtualizacao    DATETIME2         NOT NULL CONSTRAINT DF_Produtos_DataAtualizacao DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Produtos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Produtos_SKU UNIQUE (SKU),
        CONSTRAINT FK_Produtos_Categorias   FOREIGN KEY (CategoriaId)  REFERENCES dbo.Categorias (Id)   ON DELETE SET NULL,
        CONSTRAINT FK_Produtos_Fornecedores FOREIGN KEY (FornecedorId) REFERENCES dbo.Fornecedores (Id) ON DELETE SET NULL,
        CONSTRAINT CK_Produtos_PrecoVenda   CHECK (PrecoVenda >= 0),
        CONSTRAINT CK_Produtos_PrecoPromo   CHECK (PrecoPromocional IS NULL OR PrecoPromocional >= 0),
        CONSTRAINT CK_Produtos_PrecoCusto   CHECK (PrecoCusto >= 0),
        CONSTRAINT CK_Produtos_Estoque      CHECK (QuantidadeEstoque >= 0),
        CONSTRAINT CK_Produtos_EstoqueLimites CHECK (EstoqueMinimo >= 0 AND EstoqueMaximo >= 0),
        CONSTRAINT CK_Produtos_Status       CHECK (Status IN (N'Ativo', N'Inativo', N'Descontinuado')),
        INDEX IX_Produtos_Status_Nome NONCLUSTERED (Status, Nome),
        INDEX IX_Produtos_CategoriaId NONCLUSTERED (CategoriaId),
        INDEX IX_Produtos_FornecedorId NONCLUSTERED (FornecedorId)
    );
END
GO

/* ------------------------------------------------------------------ *
 *  Enderecos, carrinho e favoritos
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Enderecos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Enderecos
    (
        Id            INT IDENTITY(1,1) NOT NULL,
        UsuarioId     INT               NOT NULL,
        Nome          NVARCHAR(100)     NOT NULL CONSTRAINT DF_Enderecos_Nome DEFAULT (N'Endereço principal'),
        CEP           NVARCHAR(12)      NOT NULL,
        Rua           NVARCHAR(150)     NOT NULL,
        Numero        NVARCHAR(20)      NOT NULL,
        Complemento   NVARCHAR(100)     NULL,
        Bairro        NVARCHAR(100)     NOT NULL,
        Cidade        NVARCHAR(100)     NOT NULL,
        Estado        NVARCHAR(50)      NOT NULL,
        Pais          NVARCHAR(80)      NOT NULL CONSTRAINT DF_Enderecos_Pais DEFAULT (N'Brasil'),
        Principal     BIT               NOT NULL CONSTRAINT DF_Enderecos_Principal DEFAULT (0),
        DataCadastro  DATETIME2         NOT NULL CONSTRAINT DF_Enderecos_DataCadastro DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Enderecos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Enderecos_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        INDEX IX_Enderecos_UsuarioId_Principal NONCLUSTERED (UsuarioId, Principal),
        INDEX IX_Enderecos_CEP NONCLUSTERED (CEP)
    );
END
GO

IF OBJECT_ID(N'dbo.Carrinhos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Carrinhos
    (
        Id           INT IDENTITY(1,1) NOT NULL,
        UsuarioId    INT               NOT NULL,
        DataCriacao  DATETIME2         NOT NULL CONSTRAINT DF_Carrinhos_DataCriacao DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Carrinhos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Carrinhos_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        INDEX IX_Carrinhos_UsuarioId NONCLUSTERED (UsuarioId)
    );
END
GO

IF OBJECT_ID(N'dbo.ItensCarrinho', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ItensCarrinho
    (
        Id             INT IDENTITY(1,1) NOT NULL,
        CarrinhoId     INT               NOT NULL,
        ProdutoId      INT               NOT NULL,
        NomeProduto    NVARCHAR(200)     NOT NULL,
        Quantidade     INT               NOT NULL,
        PrecoUnitario  DECIMAL(18,2)     NOT NULL,
        CONSTRAINT PK_ItensCarrinho PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ItensCarrinho_Carrinhos FOREIGN KEY (CarrinhoId)
            REFERENCES dbo.Carrinhos (Id) ON DELETE CASCADE,
        CONSTRAINT FK_ItensCarrinho_Produtos FOREIGN KEY (ProdutoId)
            REFERENCES dbo.Produtos (Id),
        CONSTRAINT UQ_ItensCarrinho_Carrinho_Produto UNIQUE (CarrinhoId, ProdutoId),
        CONSTRAINT CK_ItensCarrinho_Quantidade CHECK (Quantidade > 0),
        CONSTRAINT CK_ItensCarrinho_Preco CHECK (PrecoUnitario >= 0),
        INDEX IX_ItensCarrinho_ProdutoId NONCLUSTERED (ProdutoId)
    );
END
GO

IF OBJECT_ID(N'dbo.Favoritos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Favoritos
    (
        Id              INT IDENTITY(1,1) NOT NULL,
        UsuarioId       INT               NOT NULL,
        ProdutoId       INT               NOT NULL,
        DataAdicionado  DATETIME2         NOT NULL CONSTRAINT DF_Favoritos_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Favoritos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Favoritos_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id) ON DELETE CASCADE,
        CONSTRAINT FK_Favoritos_Produtos FOREIGN KEY (ProdutoId)
            REFERENCES dbo.Produtos (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_Favoritos_Usuario_Produto UNIQUE (UsuarioId, ProdutoId)
    );
END
GO

/* ------------------------------------------------------------------ *
 *  Pedidos, itens, pagamentos e historicos
 * ------------------------------------------------------------------ */
IF OBJECT_ID(N'dbo.Pedidos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Pedidos
    (
        Id                 INT IDENTITY(1,1) NOT NULL,
        UsuarioId          INT               NOT NULL,
        NumeroPedido       NVARCHAR(40)      NOT NULL,
        DataPedido         DATETIME2         NOT NULL CONSTRAINT DF_Pedidos_DataPedido DEFAULT (SYSUTCDATETIME()),
        Status             NVARCHAR(30)      NOT NULL CONSTRAINT DF_Pedidos_Status DEFAULT (N'Pendente'),
        Subtotal           DECIMAL(18,2)     NOT NULL,
        Frete              DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Pedidos_Frete DEFAULT (0),
        Desconto           DECIMAL(18,2)     NOT NULL CONSTRAINT DF_Pedidos_Desconto DEFAULT (0),
        ValorTotal         DECIMAL(18,2)     NOT NULL,
        FormaPagamento     NVARCHAR(30)      NOT NULL CONSTRAINT DF_Pedidos_FormaPagamento DEFAULT (N'PIX'),
        StatusPagamento    NVARCHAR(30)      NOT NULL CONSTRAINT DF_Pedidos_StatusPagamento DEFAULT (N'Pendente'),
        EnderecoEntregaId  INT               NOT NULL,
        CONSTRAINT PK_Pedidos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Pedidos_NumeroPedido UNIQUE (NumeroPedido),
        CONSTRAINT FK_Pedidos_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id),
        CONSTRAINT FK_Pedidos_Enderecos FOREIGN KEY (EnderecoEntregaId)
            REFERENCES dbo.Enderecos (Id),
        CONSTRAINT CK_Pedidos_Status CHECK (Status IN
            (N'Pendente', N'AguardandoPagamento', N'Finalizado', N'Enviado', N'Entregue', N'Cancelado')),
        CONSTRAINT CK_Pedidos_StatusPagamento CHECK (StatusPagamento IN
            (N'Pendente', N'AguardandoPagamento', N'Pago', N'Falhou', N'Estornado', N'Cancelado')),
        CONSTRAINT CK_Pedidos_FormaPagamento CHECK (FormaPagamento IN
            (N'PIX', N'CartaoCredito', N'CartaoDebito', N'Boleto')),
        CONSTRAINT CK_Pedidos_Valores CHECK (Subtotal >= 0 AND Frete >= 0 AND Desconto >= 0
            AND ValorTotal = Subtotal + Frete - Desconto),
        INDEX IX_Pedidos_UsuarioId_DataPedido NONCLUSTERED (UsuarioId, DataPedido DESC),
        INDEX IX_Pedidos_Status NONCLUSTERED (Status) INCLUDE (DataPedido, ValorTotal),
        INDEX IX_Pedidos_EnderecoEntregaId NONCLUSTERED (EnderecoEntregaId)
    );
END
GO

IF OBJECT_ID(N'dbo.ItensPedido', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ItensPedido
    (
        Id             INT IDENTITY(1,1) NOT NULL,
        PedidoId       INT               NOT NULL,
        ProdutoId      INT               NOT NULL,
        NomeProduto    NVARCHAR(200)     NOT NULL,
        Quantidade     INT               NOT NULL,
        PrecoUnitario  DECIMAL(18,2)     NOT NULL,
        CONSTRAINT PK_ItensPedido PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ItensPedido_Pedidos FOREIGN KEY (PedidoId)
            REFERENCES dbo.Pedidos (Id) ON DELETE CASCADE,
        CONSTRAINT FK_ItensPedido_Produtos FOREIGN KEY (ProdutoId)
            REFERENCES dbo.Produtos (Id),
        CONSTRAINT UQ_ItensPedido_Pedido_Produto UNIQUE (PedidoId, ProdutoId),
        CONSTRAINT CK_ItensPedido_Quantidade CHECK (Quantidade > 0),
        CONSTRAINT CK_ItensPedido_Preco CHECK (PrecoUnitario >= 0),
        INDEX IX_ItensPedido_ProdutoId NONCLUSTERED (ProdutoId) INCLUDE (Quantidade, PrecoUnitario)
    );
END
GO

IF OBJECT_ID(N'dbo.Pagamentos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Pagamentos
    (
        Id                INT IDENTITY(1,1) NOT NULL,
        PedidoId          INT               NOT NULL,
        FormaPagamento    NVARCHAR(30)      NOT NULL,
        Status            NVARCHAR(30)      NOT NULL CONSTRAINT DF_Pagamentos_Status DEFAULT (N'Pendente'),
        Valor             DECIMAL(18,2)     NOT NULL,
        CodigoReferencia  NVARCHAR(100)     NOT NULL CONSTRAINT DF_Pagamentos_Codigo DEFAULT (N''),
        Gateway           NVARCHAR(50)      NULL,
        TokenCartao       NVARCHAR(100)     NULL,
        DataPagamento     DATETIME2         NOT NULL CONSTRAINT DF_Pagamentos_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Pagamentos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Pagamentos_Pedidos FOREIGN KEY (PedidoId)
            REFERENCES dbo.Pedidos (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_Pagamentos_PedidoId UNIQUE (PedidoId),
        CONSTRAINT CK_Pagamentos_Status CHECK (Status IN
            (N'Pendente', N'AguardandoPagamento', N'Pago', N'Falhou', N'Estornado', N'Cancelado')),
        CONSTRAINT CK_Pagamentos_Valor CHECK (Valor >= 0)
    );
END
GO

IF OBJECT_ID(N'dbo.HistoricoPagamentos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HistoricoPagamentos
    (
        Id              INT IDENTITY(1,1) NOT NULL,
        PedidoId        INT               NOT NULL,
        StatusAnterior  NVARCHAR(30)      NOT NULL,
        StatusAtual     NVARCHAR(30)      NOT NULL,
        Observacao      NVARCHAR(500)     NOT NULL CONSTRAINT DF_HistoricoPagamentos_Obs DEFAULT (N''),
        DataRegistro    DATETIME2         NOT NULL CONSTRAINT DF_HistoricoPagamentos_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_HistoricoPagamentos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_HistoricoPagamentos_Pedidos FOREIGN KEY (PedidoId)
            REFERENCES dbo.Pedidos (Id) ON DELETE CASCADE,
        INDEX IX_HistoricoPagamentos_PedidoId NONCLUSTERED (PedidoId, DataRegistro)
    );
END
GO

IF OBJECT_ID(N'dbo.MovimentacoesEstoque', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MovimentacoesEstoque
    (
        Id                 INT IDENTITY(1,1) NOT NULL,
        ProdutoId          INT               NOT NULL,
        TipoMovimentacao   NVARCHAR(20)      NOT NULL,
        Quantidade         INT               NOT NULL,
        EstoqueAnterior    INT               NOT NULL,
        EstoqueAtual       INT               NOT NULL,
        Motivo             NVARCHAR(300)     NOT NULL CONSTRAINT DF_MovimentacoesEstoque_Motivo DEFAULT (N''),
        UsuarioId          INT               NOT NULL,
        Data               DATETIME2         NOT NULL CONSTRAINT DF_MovimentacoesEstoque_Data DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_MovimentacoesEstoque PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_MovimentacoesEstoque_Produtos FOREIGN KEY (ProdutoId)
            REFERENCES dbo.Produtos (Id),
        CONSTRAINT FK_MovimentacoesEstoque_Usuarios FOREIGN KEY (UsuarioId)
            REFERENCES dbo.Usuarios (Id),
        CONSTRAINT CK_MovimentacoesEstoque_Tipo CHECK (TipoMovimentacao IN (N'Entrada', N'Saida', N'Ajuste')),
        CONSTRAINT CK_MovimentacoesEstoque_Qtde CHECK (Quantidade >= 0 AND EstoqueAnterior >= 0 AND EstoqueAtual >= 0),
        INDEX IX_MovimentacoesEstoque_Produto_Data NONCLUSTERED (ProdutoId, Data DESC),
        INDEX IX_MovimentacoesEstoque_UsuarioId NONCLUSTERED (UsuarioId)
    );
END
GO

/* ------------------------------------------------------------------ *
 *  Funcao escalar: preco efetivo (promocional somente se menor que o preco)
 *  Espelha Produto.PrecoEfetivo da aplicacao.
 * ------------------------------------------------------------------ */
CREATE OR ALTER FUNCTION dbo.fn_PrecoEfetivo (@PrecoVenda DECIMAL(18,2), @PrecoPromocional DECIMAL(18,2))
RETURNS DECIMAL(18,2)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CASE
               WHEN @PrecoPromocional IS NOT NULL
                    AND @PrecoPromocional > 0
                    AND @PrecoPromocional < @PrecoVenda
                   THEN @PrecoPromocional
               ELSE @PrecoVenda
           END;
END
GO

/* ------------------------------------------------------------------ *
 *  Views de apoio (usadas por procedures e relatorios)
 * ------------------------------------------------------------------ */
CREATE OR ALTER VIEW dbo.vw_CatalogoProdutos
AS
SELECT  p.Id,
        p.Nome,
        p.SKU,
        p.Descricao,
        p.Marca,
        p.EdicaoEspecial,
        p.Imagem,
        p.PrecoVenda,
        p.PrecoPromocional,
        dbo.fn_PrecoEfetivo(p.PrecoVenda, p.PrecoPromocional) AS PrecoEfetivo,
        p.QuantidadeEstoque,
        CAST(CASE WHEN p.QuantidadeEstoque > 0 THEN 1 ELSE 0 END AS BIT) AS Disponivel,
        c.Nome AS Categoria
FROM    dbo.Produtos p
        LEFT JOIN dbo.Categorias c ON c.Id = p.CategoriaId
WHERE   p.Status = N'Ativo';
GO

CREATE OR ALTER VIEW dbo.vw_VendasDetalhadas
AS
SELECT  p.Id                AS PedidoId,
        p.NumeroPedido,
        p.DataPedido,
        p.Status            AS StatusPedido,
        p.StatusPagamento,
        p.FormaPagamento,
        p.UsuarioId,
        ip.ProdutoId,
        ip.NomeProduto,
        ip.Quantidade,
        ip.PrecoUnitario,
        CAST(ip.Quantidade * ip.PrecoUnitario AS DECIMAL(18,2)) AS TotalItem
FROM    dbo.Pedidos p
        INNER JOIN dbo.ItensPedido ip ON ip.PedidoId = p.Id;
GO

/* ############ 02_triggers.sql ############ */
/* =====================================================================
   Felibow - Banco de dados da loja (SQL Server 2017+)
   PIM IV - Etapa 7 - Projeto do Banco de Dados
   Arquivo 02: TRIGGERS

   Regras de negocio garantidas pelo proprio banco, independentemente de
   qual cliente (Web MVC, API REST, Mobile ou SQL manual) faz a alteracao.

   Os triggers complementam a aplicacao sem duplicar o que ela ja faz:
   a baixa de estoque na compra continua sendo feita pelo EstoqueService.

   Resumo:
     trg_Produtos_AtualizaData              - carimba DataAtualizacao do produto
     trg_Produtos_AlertaEstoqueMinimo       - avisa administradores (Notificacoes)
     trg_Pagamentos_SincronizaPedido        - pagamento -> status do pedido
     trg_Pedidos_HistoricoPagamento         - historico de StatusPagamento
     trg_Pedidos_ControlaStatus             - valida transicao, estorna estoque e pagamento
     trg_Pedidos_NotificaCliente            - notifica o cliente (envio/entrega/cancelamento)
     trg_ItensPedido_Imutavel               - itens nao mudam depois que o pedido sai de Pendente
   ===================================================================== */

SET NOCOUNT ON;
GO

USE FelibowLoja;
GO

/* ------------------------------------------------------------------ *
 *  PRODUTOS
 * ------------------------------------------------------------------ */
CREATE OR ALTER TRIGGER dbo.trg_Produtos_AtualizaData
ON dbo.Produtos
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Se quem atualizou informou DataAtualizacao explicitamente, respeita o valor.
    IF UPDATE(DataAtualizacao) RETURN;

    UPDATE p
       SET DataAtualizacao = SYSUTCDATETIME()
      FROM dbo.Produtos p
           INNER JOIN inserted i ON i.Id = p.Id;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_Produtos_AlertaEstoqueMinimo
ON dbo.Produtos
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(QuantidadeEstoque) RETURN;

    -- Dispara somente quando o estoque ATRAVESSA o minimo (evita repeticao de alertas).
    INSERT INTO dbo.Notificacoes (UsuarioId, Categoria, Titulo, Mensagem, Url, CriadaEm)
    SELECT  a.Id,
            N'EstoqueBaixo',
            N'Estoque baixo',
            CONCAT(N'O produto "', i.Nome, N'" (SKU ', i.SKU, N') chegou a ',
                   i.QuantidadeEstoque, N' unidade(s); o minimo configurado e ',
                   i.EstoqueMinimo, N'.'),
            N'/api/v1/admin/estoque/baixo',
            SYSUTCDATETIME()
      FROM  inserted i
            INNER JOIN deleted d ON d.Id = i.Id
            CROSS JOIN (SELECT Id FROM dbo.Usuarios WHERE Tipo = N'Admin' AND Status = N'Ativo') a
     WHERE  i.Status = N'Ativo'
       AND  i.EstoqueMinimo > 0
       AND  i.QuantidadeEstoque <= i.EstoqueMinimo
       AND  d.QuantidadeEstoque  >  d.EstoqueMinimo;
END
GO

/* ------------------------------------------------------------------ *
 *  PAGAMENTOS -> PEDIDOS
 * ------------------------------------------------------------------ */
CREATE OR ALTER TRIGGER dbo.trg_Pagamentos_SincronizaPedido
ON dbo.Pagamentos
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Mantem Pedidos.StatusPagamento igual ao status do pagamento e,
    -- quando o pagamento e confirmado, finaliza o pedido que ainda aguardava.
    UPDATE p
       SET p.StatusPagamento = i.Status,
           p.Status = CASE
                          WHEN i.Status = N'Pago'
                               AND p.Status IN (N'Pendente', N'AguardandoPagamento')
                              THEN N'Finalizado'
                          ELSE p.Status
                      END
      FROM dbo.Pedidos p
           INNER JOIN inserted i ON i.PedidoId = p.Id
     WHERE p.StatusPagamento <> i.Status
        OR (i.Status = N'Pago' AND p.Status IN (N'Pendente', N'AguardandoPagamento'));
END
GO

/* ------------------------------------------------------------------ *
 *  PEDIDOS
 * ------------------------------------------------------------------ */
CREATE OR ALTER TRIGGER dbo.trg_Pedidos_HistoricoPagamento
ON dbo.Pedidos
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(StatusPagamento) RETURN;

    INSERT INTO dbo.HistoricoPagamentos (PedidoId, StatusAnterior, StatusAtual, Observacao, DataRegistro)
    SELECT  i.Id,
            d.StatusPagamento,
            i.StatusPagamento,
            N'Registrado automaticamente pelo banco de dados.',
            SYSUTCDATETIME()
      FROM  inserted i
            INNER JOIN deleted d ON d.Id = i.Id
     WHERE  i.StatusPagamento <> d.StatusPagamento;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_Pedidos_ControlaStatus
ON dbo.Pedidos
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(Status) RETURN;

    -- 1) Transicoes validas. Estados finais (Entregue, Cancelado) nao mudam mais.
    IF EXISTS
    (
        SELECT 1
          FROM inserted i
               INNER JOIN deleted d ON d.Id = i.Id
         WHERE i.Status <> d.Status
           AND NOT
               (
                   (d.Status = N'Pendente'            AND i.Status IN (N'AguardandoPagamento', N'Finalizado', N'Cancelado'))
                OR (d.Status = N'AguardandoPagamento' AND i.Status IN (N'Finalizado', N'Cancelado'))
                OR (d.Status = N'Finalizado'          AND i.Status IN (N'Enviado', N'Cancelado'))
                OR (d.Status = N'Enviado'             AND i.Status IN (N'Entregue'))
               )
    )
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50002, N'Transicao de status de pedido invalida.', 1;
    END

    -- 2) Cancelamento: devolve o estoque e cancela/estorna o pagamento.
    IF NOT EXISTS
    (
        SELECT 1
          FROM inserted i
               INNER JOIN deleted d ON d.Id = i.Id
         WHERE i.Status = N'Cancelado' AND d.Status <> N'Cancelado'
    )
        RETURN;

    DECLARE @mov TABLE
    (
        ProdutoId   INT,
        Anterior    INT,
        Atual       INT,
        Quantidade  INT,
        UsuarioId   INT,
        Pedidos     NVARCHAR(MAX)
    );

    ;WITH cancelados AS
    (
        SELECT i.Id, i.UsuarioId, i.NumeroPedido
          FROM inserted i
               INNER JOIN deleted d ON d.Id = i.Id
         WHERE i.Status = N'Cancelado' AND d.Status <> N'Cancelado'
    ),
    agregado AS
    (
        SELECT  ip.ProdutoId,
                SUM(ip.Quantidade)                  AS Quantidade,
                MIN(c.UsuarioId)                    AS UsuarioId,
                STRING_AGG(c.NumeroPedido, N', ')   AS Pedidos
          FROM  cancelados c
                INNER JOIN dbo.ItensPedido ip ON ip.PedidoId = c.Id
         GROUP BY ip.ProdutoId
    )
    UPDATE p
       SET p.QuantidadeEstoque = p.QuantidadeEstoque + a.Quantidade
    OUTPUT  inserted.Id, deleted.QuantidadeEstoque, inserted.QuantidadeEstoque,
            a.Quantidade, a.UsuarioId, a.Pedidos
       INTO @mov (ProdutoId, Anterior, Atual, Quantidade, UsuarioId, Pedidos)
      FROM dbo.Produtos p
           INNER JOIN agregado a ON a.ProdutoId = p.Id;

    INSERT INTO dbo.MovimentacoesEstoque
        (ProdutoId, TipoMovimentacao, Quantidade, EstoqueAnterior, EstoqueAtual, Motivo, UsuarioId, Data)
    SELECT  m.ProdutoId, N'Entrada', m.Quantidade, m.Anterior, m.Atual,
            LEFT(CONCAT(N'Estorno por cancelamento do pedido ', m.Pedidos), 300),
            m.UsuarioId, SYSUTCDATETIME()
      FROM  @mov m;

    -- Pagamento: pago -> Estornado; demais -> Cancelado.
    -- (o trigger dbo.trg_Pagamentos_SincronizaPedido atualiza Pedidos.StatusPagamento)
    UPDATE pg
       SET pg.Status = CASE WHEN pg.Status = N'Pago' THEN N'Estornado' ELSE N'Cancelado' END
      FROM dbo.Pagamentos pg
           INNER JOIN inserted i ON i.Id = pg.PedidoId
           INNER JOIN deleted  d ON d.Id = i.Id
     WHERE i.Status = N'Cancelado' AND d.Status <> N'Cancelado'
       AND pg.Status NOT IN (N'Estornado', N'Cancelado');
END
GO

CREATE OR ALTER TRIGGER dbo.trg_Pedidos_NotificaCliente
ON dbo.Pedidos
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT UPDATE(Status) RETURN;

    INSERT INTO dbo.Notificacoes (UsuarioId, Categoria, Titulo, Mensagem, Url, CriadaEm)
    SELECT  i.UsuarioId,
            N'AtualizacoesPedidos',
            CASE i.Status
                WHEN N'Enviado'   THEN N'Pedido enviado'
                WHEN N'Entregue'  THEN N'Pedido entregue'
                ELSE N'Pedido cancelado'
            END,
            CONCAT(N'O pedido ', i.NumeroPedido, N' agora esta com status ', i.Status, N'.'),
            CONCAT(N'/api/v1/pedidos/', i.Id),
            SYSUTCDATETIME()
      FROM  inserted i
            INNER JOIN deleted d ON d.Id = i.Id
            LEFT JOIN dbo.PerfisConta pc ON pc.UsuarioId = i.UsuarioId
     WHERE  i.Status <> d.Status
       AND  i.Status IN (N'Enviado', N'Entregue', N'Cancelado')
       AND  COALESCE(pc.AtualizacoesPedidos, 1) = 1;
END
GO

/* ------------------------------------------------------------------ *
 *  ITENS DO PEDIDO
 * ------------------------------------------------------------------ */
CREATE OR ALTER TRIGGER dbo.trg_ItensPedido_Imutavel
ON dbo.ItensPedido
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Depois que o pedido deixa de ser "Pendente", precos e quantidades ficam congelados
    -- (rastreabilidade fiscal e financeira).
    IF EXISTS
    (
        SELECT 1
          FROM inserted i
               INNER JOIN deleted d ON d.Id = i.Id
               INNER JOIN dbo.Pedidos p ON p.Id = i.PedidoId
         WHERE p.Status <> N'Pendente'
           AND (   i.Quantidade    <> d.Quantidade
                OR i.PrecoUnitario <> d.PrecoUnitario
                OR i.ProdutoId     <> d.ProdutoId)
    )
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50002, N'Itens de um pedido ja processado nao podem ser alterados.', 1;
    END
END
GO

/* ############ 03_procedures.sql ############ */
/* =====================================================================
   Felibow - Banco de dados da loja (SQL Server 2017+)
   PIM IV - Etapa 7 - Projeto do Banco de Dados
   Arquivo 03: STORED PROCEDURES

   Convencao de erros (THROW):
     50001  registro nao encontrado           -> HTTP 404 na API
     50002  regra de negocio violada          -> HTTP 409 na API
     50003  operacao nao permitida            -> HTTP 403 na API
     50004  parametro invalido                -> HTTP 400 na API

   Procedures consumidas pela API REST (loja_s/Api) e, portanto, pelo app Mobile:
     sp_ObterResumoCliente          GET  /api/v1/conta/resumo
     sp_CancelarPedidoCliente       POST /api/v1/pedidos/{id}/cancelar
     sp_AtualizarStatusPedido       PUT  /api/v1/admin/pedidos/{id}/status
     sp_AjustarEstoque              POST /api/v1/admin/estoque/ajustes
     sp_RelatorioVendasPorPeriodo   GET  /api/v1/admin/relatorios/vendas
     sp_ProdutosMaisVendidos        GET  /api/v1/admin/relatorios/mais-vendidos
     sp_ProdutosEstoqueBaixo        GET  /api/v1/admin/estoque/baixo
   Procedures de operacao/manutencao (uso por DBA):
     sp_PromoverAdministrador, sp_LimparDadosExpirados
   ===================================================================== */

SET NOCOUNT ON;
GO

USE FelibowLoja;
GO

/* ------------------------------------------------------------------ *
 *  Pedidos
 * ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.sp_AtualizarStatusPedido
    @PedidoId   INT,
    @NovoStatus NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @atual NVARCHAR(30);

    SELECT @atual = Status FROM dbo.Pedidos WHERE Id = @PedidoId;

    IF @atual IS NULL
        THROW 50001, N'Pedido nao encontrado.', 1;

    IF @NovoStatus NOT IN (N'Pendente', N'AguardandoPagamento', N'Finalizado', N'Enviado', N'Entregue', N'Cancelado')
        THROW 50004, N'Status de pedido invalido.', 1;

    IF @atual <> @NovoStatus
       AND NOT
           (
               (@atual = N'Pendente'            AND @NovoStatus IN (N'AguardandoPagamento', N'Finalizado', N'Cancelado'))
            OR (@atual = N'AguardandoPagamento' AND @NovoStatus IN (N'Finalizado', N'Cancelado'))
            OR (@atual = N'Finalizado'          AND @NovoStatus IN (N'Enviado', N'Cancelado'))
            OR (@atual = N'Enviado'             AND @NovoStatus = N'Entregue')
           )
        THROW 50002, N'Transicao de status de pedido invalida.', 1;

    IF @atual <> @NovoStatus
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            -- Triggers: historico, notificacao ao cliente e, no cancelamento,
            -- estorno de estoque e pagamento.
            UPDATE dbo.Pedidos SET Status = @NovoStatus WHERE Id = @PedidoId;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH
    END

    SELECT  p.Id AS PedidoId, p.NumeroPedido, p.Status, p.StatusPagamento
      FROM  dbo.Pedidos p
     WHERE  p.Id = @PedidoId;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_CancelarPedidoCliente
    @PedidoId  INT,
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @dono INT, @atual NVARCHAR(30);

    SELECT @dono = UsuarioId, @atual = Status FROM dbo.Pedidos WHERE Id = @PedidoId;

    -- Nao revela a existencia de pedidos de outros clientes.
    IF @dono IS NULL OR @dono <> @UsuarioId
        THROW 50001, N'Pedido nao encontrado.', 1;

    IF @atual NOT IN (N'Pendente', N'AguardandoPagamento', N'Finalizado')
        THROW 50002, N'Este pedido nao pode mais ser cancelado.', 1;

    EXEC dbo.sp_AtualizarStatusPedido @PedidoId = @PedidoId, @NovoStatus = N'Cancelado';
END
GO

/* ------------------------------------------------------------------ *
 *  Estoque
 * ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.sp_AjustarEstoque
    @ProdutoId  INT,
    @Tipo       NVARCHAR(20),          -- Entrada | Saida | Ajuste (Ajuste = define o saldo)
    @Quantidade INT,
    @Motivo     NVARCHAR(300),
    @UsuarioId  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Tipo NOT IN (N'Entrada', N'Saida', N'Ajuste')
        THROW 50004, N'Tipo de movimentacao invalido. Use Entrada, Saida ou Ajuste.', 1;

    IF @Quantidade IS NULL OR @Quantidade < 0 OR (@Tipo <> N'Ajuste' AND @Quantidade = 0)
        THROW 50004, N'Quantidade invalida.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Usuarios WHERE Id = @UsuarioId)
        THROW 50001, N'Usuario nao encontrado.', 1;

    DECLARE @anterior INT, @novo INT;

    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT @anterior = QuantidadeEstoque
          FROM dbo.Produtos WITH (UPDLOCK, HOLDLOCK)
         WHERE Id = @ProdutoId;

        IF @anterior IS NULL
            THROW 50001, N'Produto nao encontrado.', 1;

        SET @novo = CASE @Tipo
                        WHEN N'Entrada' THEN @anterior + @Quantidade
                        WHEN N'Saida'   THEN @anterior - @Quantidade
                        ELSE @Quantidade
                    END;

        IF @novo < 0
            THROW 50002, N'Estoque insuficiente para esta saida.', 1;

        UPDATE dbo.Produtos SET QuantidadeEstoque = @novo WHERE Id = @ProdutoId;

        INSERT INTO dbo.MovimentacoesEstoque
            (ProdutoId, TipoMovimentacao, Quantidade, EstoqueAnterior, EstoqueAtual, Motivo, UsuarioId, Data)
        VALUES
            (@ProdutoId, @Tipo,
             CASE WHEN @Tipo = N'Ajuste' THEN ABS(@novo - @anterior) ELSE @Quantidade END,
             @anterior, @novo, ISNULL(@Motivo, N''), @UsuarioId, SYSUTCDATETIME());

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT  p.Id AS ProdutoId, p.SKU, p.Nome,
            @anterior AS EstoqueAnterior, @novo AS EstoqueAtual
      FROM  dbo.Produtos p
     WHERE  p.Id = @ProdutoId;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_ProdutosEstoqueBaixo
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  p.Id,
            p.SKU,
            p.Nome,
            p.QuantidadeEstoque,
            p.EstoqueMinimo,
            p.EstoqueMaximo,
            CASE WHEN p.QuantidadeEstoque = 0 THEN N'Esgotado' ELSE N'Baixo' END AS Situacao,
            CASE WHEN p.EstoqueMaximo > p.QuantidadeEstoque
                 THEN p.EstoqueMaximo - p.QuantidadeEstoque ELSE 0 END           AS SugestaoReposicao
      FROM  dbo.Produtos p
     WHERE  p.Status = N'Ativo'
       AND (p.QuantidadeEstoque = 0
            OR (p.EstoqueMinimo > 0 AND p.QuantidadeEstoque <= p.EstoqueMinimo))
     ORDER BY p.QuantidadeEstoque, p.Nome;
END
GO

/* ------------------------------------------------------------------ *
 *  Relatorios
 * ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.sp_RelatorioVendasPorPeriodo
    @De  DATE = NULL,
    @Ate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @Ate = COALESCE(@Ate, CAST(SYSUTCDATETIME() AS DATE));
    SET @De  = COALESCE(@De, DATEADD(DAY, -30, @Ate));

    IF @De > @Ate
        THROW 50004, N'A data inicial nao pode ser maior que a data final.', 1;

    SELECT  CAST(p.DataPedido AS DATE)                    AS Dia,
            COUNT(*)                                      AS QuantidadePedidos,
            SUM(p.Subtotal)                               AS Subtotal,
            SUM(p.Frete)                                  AS Frete,
            SUM(p.Desconto)                               AS Desconto,
            SUM(p.ValorTotal)                             AS Total,
            CAST(AVG(p.ValorTotal) AS DECIMAL(18,2))      AS TicketMedio
      FROM  dbo.Pedidos p
     WHERE  p.Status <> N'Cancelado'
       AND  p.DataPedido >= @De
       AND  p.DataPedido <  DATEADD(DAY, 1, @Ate)
     GROUP BY CAST(p.DataPedido AS DATE)
     ORDER BY Dia;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_ProdutosMaisVendidos
    @Top INT  = 10,
    @De  DATE = NULL,
    @Ate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Top < 1 OR @Top > 100
        THROW 50004, N'O parametro Top deve estar entre 1 e 100.', 1;

    SET @Ate = COALESCE(@Ate, CAST(SYSUTCDATETIME() AS DATE));
    SET @De  = COALESCE(@De, DATEADD(DAY, -30, @Ate));

    IF @De > @Ate
        THROW 50004, N'A data inicial nao pode ser maior que a data final.', 1;

    SELECT TOP (@Top)
            v.ProdutoId,
            MAX(v.NomeProduto)  AS NomeProduto,
            SUM(v.Quantidade)   AS QuantidadeVendida,
            SUM(v.TotalItem)    AS Receita
      FROM  dbo.vw_VendasDetalhadas v
     WHERE  v.StatusPedido <> N'Cancelado'
       AND  v.DataPedido >= @De
       AND  v.DataPedido <  DATEADD(DAY, 1, @Ate)
     GROUP BY v.ProdutoId
     ORDER BY SUM(v.Quantidade) DESC, SUM(v.TotalItem) DESC;
END
GO

/* ------------------------------------------------------------------ *
 *  Conta do cliente
 * ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.sp_ObterResumoCliente
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Usuarios WHERE Id = @UsuarioId)
        THROW 50001, N'Usuario nao encontrado.', 1;

    SELECT
        (SELECT COUNT(*) FROM dbo.Pedidos
          WHERE UsuarioId = @UsuarioId AND Status <> N'Cancelado')                      AS QuantidadePedidos,
        (SELECT COUNT(*) FROM dbo.Pedidos
          WHERE UsuarioId = @UsuarioId
            AND Status IN (N'Pendente', N'AguardandoPagamento', N'Finalizado', N'Enviado')) AS PedidosEmAberto,
        (SELECT COALESCE(SUM(ValorTotal), 0) FROM dbo.Pedidos
          WHERE UsuarioId = @UsuarioId AND StatusPagamento = N'Pago'
            AND Status <> N'Cancelado')                                                 AS TotalGasto,
        (SELECT COUNT(*) FROM dbo.Favoritos WHERE UsuarioId = @UsuarioId)               AS Favoritos,
        (SELECT COUNT(*) FROM dbo.Notificacoes
          WHERE UsuarioId = @UsuarioId AND LidaEm IS NULL)                              AS NotificacoesNaoLidas,
        (SELECT COALESCE(SUM(i.Quantidade), 0)
           FROM dbo.Carrinhos c INNER JOIN dbo.ItensCarrinho i ON i.CarrinhoId = c.Id
          WHERE c.UsuarioId = @UsuarioId)                                               AS ItensNoCarrinho;
END
GO

/* ------------------------------------------------------------------ *
 *  Operacao / manutencao (DBA)
 * ------------------------------------------------------------------ */
CREATE OR ALTER PROCEDURE dbo.sp_PromoverAdministrador
    @Email NVARCHAR(254)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Usuarios SET Tipo = N'Admin' WHERE Email = @Email;

    IF @@ROWCOUNT = 0
        THROW 50001, N'Usuario nao encontrado.', 1;

    SELECT Id, Nome, Email, Tipo FROM dbo.Usuarios WHERE Email = @Email;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_LimparDadosExpirados
    @DiasSessaoInativa INT = 30,
    @DiasTokens        INT = 7
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @sessoes INT, @tokens INT, @carrinhos INT;

    BEGIN TRY
        BEGIN TRANSACTION;

        DELETE FROM dbo.SessoesConta
         WHERE UltimaAtividade < DATEADD(DAY, -@DiasSessaoInativa, SYSUTCDATETIME());
        SET @sessoes = @@ROWCOUNT;

        DELETE FROM dbo.TokensConta
         WHERE ExpiraEm < DATEADD(DAY, -@DiasTokens, SYSUTCDATETIME());
        SET @tokens = @@ROWCOUNT;

        DELETE c
          FROM dbo.Carrinhos c
         WHERE c.DataCriacao < DATEADD(DAY, -30, SYSUTCDATETIME())
           AND NOT EXISTS (SELECT 1 FROM dbo.ItensCarrinho i WHERE i.CarrinhoId = c.Id);
        SET @carrinhos = @@ROWCOUNT;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH

    SELECT @sessoes AS SessoesRemovidas, @tokens AS TokensRemovidos, @carrinhos AS CarrinhosVaziosRemovidos;
END
GO

/* ############ 04_seed.sql ############ */
/* =====================================================================
   Felibow - Banco de dados da loja (SQL Server 2017+)
   PIM IV - Etapa 7 - Projeto do Banco de Dados
   Arquivo 04: DADOS INICIAIS (catalogo)

   - Idempotente: so insere o que ainda nao existe (chave: Nome / CNPJ / SKU).
   - Nao cria usuarios nem senhas. Clientes se cadastram pelo site ou pelo app;
     para criar um administrador use dbo.sp_PromoverAdministrador.
   - Os CNPJ abaixo sao ficticios (dados de demonstracao).
   - Arquivo em UTF-8: ao usar sqlcmd, informe  -f 65001.
   ===================================================================== */

SET NOCOUNT ON;
GO

USE FelibowLoja;
GO

INSERT INTO dbo.Categorias (Nome, Descricao)
SELECT v.Nome, v.Descricao
  FROM (VALUES
        (N'Creatina',        N'Creatinas para força e performance'),
        (N'Whey Protein',    N'Proteínas para recuperação muscular'),
        (N'Vitaminas',       N'Vitaminas e multivitamínicos'),
        (N'Ômega 3',         N'Ácidos graxos essenciais'),
        (N'Minerais',        N'Minerais para a rotina esportiva'),
        (N'Aminoácidos',     N'Aminoácidos e recuperação'),
        (N'Saúde digestiva', N'Equilíbrio intestinal'),
        (N'Pré-treino',      N'Energia e foco para o treino')
       ) AS v (Nome, Descricao)
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Categorias c WHERE c.Nome = v.Nome);
GO

INSERT INTO dbo.Fornecedores (RazaoSocial, CNPJ, Email, Telefone)
SELECT v.RazaoSocial, v.CNPJ, v.Email, v.Telefone
  FROM (VALUES
        (N'NutriSul Distribuidora Ltda',   N'11.111.111/0001-11', N'contato@nutrisul.example',  N'(11) 4000-0001'),
        (N'VitaPrime Suplementos S.A.',    N'22.222.222/0001-22', N'contato@vitaprime.example', N'(11) 4000-0002')
       ) AS v (RazaoSocial, CNPJ, Email, Telefone)
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Fornecedores f WHERE f.CNPJ = v.CNPJ);
GO

INSERT INTO dbo.Produtos
    (Nome, SKU, PrecoVenda, PrecoCusto, QuantidadeEstoque, EstoqueMinimo, EstoqueMaximo,
     CategoriaId, FornecedorId, Marca, EdicaoEspecial, Descricao, Imagem, Status)
SELECT  v.Nome, v.SKU, v.PrecoVenda, v.PrecoCusto, v.Estoque, 5, 50,
        c.Id, f.Id, N'Felibow', v.EdicaoEspecial, v.Descricao, v.Imagem, N'Ativo'
  FROM (VALUES
        (N'VitaPrime — Creatina Monohidratada 300 g', N'PD-001', 149.90, 82.00, 24, N'Creatina', NULL, N'Creatina monohidratada pura, em pó, sem sabor. Suplemento alimentar para quem treina e busca apoio para força e desempenho. Fácil de misturar em água ou suco.', N'~/img/produtos/creatina.png'),
        (N'WheyCore — Whey Protein Chocolate 900 g', N'DW-002', 169.90, 95.00, 18, N'Whey Protein', NULL, N'Proteína do soro do leite com sabor chocolate. Suplemento alimentar para complementar a ingestão de proteínas na rotina de treino. Prático para o pós-treino.', N'~/img/produtos/whey.png'),
        (N'MultiVida — Multivitamínico 60 cápsulas', N'CE-003', 99.90, 52.00, 20, N'Vitaminas', NULL, N'Combinação de vitaminas e minerais para complementar a alimentação. Suplemento alimentar para a rotina do dia a dia.', N'~/img/produtos/multivitaminico.png'),
        (N'OceanPure — Ômega 3 120 cápsulas', N'BS-004', 129.90, 70.00, 14, N'Ômega 3', NULL, N'Óleo de peixe com EPA e DHA em cápsulas. Suplemento alimentar para complementar a dieta com ácidos graxos ômega 3.', N'~/img/produtos/omega3.png'),
        (N'MagnaBalance — Magnésio 60 cápsulas', N'MB-005', 89.90, 46.00, 16, N'Minerais', NULL, N'Suplemento alimentar de magnésio para complementar a dieta. Indicado para quem busca equilíbrio nutricional na rotina.', N'~/img/produtos/magnesio.png'),
        (N'SolVita — Vitamina D3 + K2 60 cápsulas', N'SP-006', 79.90, 40.00, 18, N'Vitaminas', NULL, N'Associação das vitaminas D3 e K2 em cápsulas. Suplemento alimentar para complementar a alimentação.', N'~/img/produtos/d3k2.png'),
        (N'GlutaFlow — Glutamina 300 g', N'MR-007', 109.90, 58.00, 12, N'Aminoácidos', NULL, N'Glutamina em pó para quem treina e quer complementar a nutrição esportiva. Pode ser misturada em água ou shakes.', N'~/img/produtos/glutamina.png'),
        (N'DuoPower — Pré-treino Frutas Vermelhas', N'DP-009', 94.90, 50.00, 15, N'Pré-treino', NULL, N'Suplemento alimentar em pó com cafeína, beta-alanina e taurina, no sabor frutas vermelhas. Para quem busca um complemento para a rotina de treino.', N'~/img/produtos/duopower.png'),
        (N'TurboVolt — Pré-treino Blueberry', N'TV-010', 99.90, 54.00, 15, N'Pré-treino', NULL, N'Suplemento alimentar em pó com cafeína, creatina e beta-alanina, no sabor blueberry. Para quem busca apoio na rotina de treino.', N'~/img/produtos/turbovolt.png')
       ) AS v (Nome, SKU, PrecoVenda, PrecoCusto, Estoque, Categoria, EdicaoEspecial, Descricao, Imagem)
       INNER JOIN dbo.Categorias c ON c.Nome = v.Categoria
       CROSS JOIN (SELECT TOP (1) Id FROM dbo.Fornecedores ORDER BY Id) f
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Produtos p WHERE p.SKU = v.SKU);
GO
