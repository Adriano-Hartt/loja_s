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
