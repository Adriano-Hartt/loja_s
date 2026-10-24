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
