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
