/* =====================================================================
   Felibow - Banco de dados da loja (SQL Server 2017+)
   PIM IV - Etapa 7 - Projeto do Banco de Dados
   Arquivo 05: TESTES DE TRIGGERS E PROCEDURES

   Executa tudo dentro de uma transacao que e SEMPRE desfeita no final:
   nenhum dado de teste permanece no banco. Cada verificacao imprime OK ou
   interrompe o script com a mensagem FALHA.

   Execute depois dos arquivos 01 a 04.
   ===================================================================== */

SET NOCOUNT ON;
GO

USE FelibowLoja;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @cli INT, @adm INT, @end INT, @prod INT, @ped INT, @tmp INT, @txt NVARCHAR(400);

    /* ---------- massa de dados de teste ---------- */
    INSERT INTO dbo.Usuarios (Nome, Email, SenhaHash, Tipo)
    VALUES (N'Cliente de Teste', N'cliente.teste@example.test', N'hash-de-teste', N'Cliente');
    SET @cli = SCOPE_IDENTITY();

    INSERT INTO dbo.Usuarios (Nome, Email, SenhaHash, Tipo)
    VALUES (N'Admin de Teste', N'admin.teste@example.test', N'hash-de-teste', N'Admin');
    SET @adm = SCOPE_IDENTITY();

    INSERT INTO dbo.Enderecos (UsuarioId, CEP, Rua, Numero, Bairro, Cidade, Estado)
    VALUES (@cli, N'01001-000', N'Praça da Sé', N'1', N'Sé', N'São Paulo', N'SP');
    SET @end = SCOPE_IDENTITY();

    INSERT INTO dbo.Produtos (Nome, SKU, PrecoVenda, QuantidadeEstoque, EstoqueMinimo, EstoqueMaximo)
    VALUES (N'Produto de Teste', N'TST-001', 100.00, 10, 3, 50);
    SET @prod = SCOPE_IDENTITY();

    INSERT INTO dbo.Pedidos (UsuarioId, NumeroPedido, Subtotal, Frete, Desconto, ValorTotal,
                             FormaPagamento, StatusPagamento, Status, EnderecoEntregaId)
    VALUES (@cli, N'TST-0001', 200.00, 19.90, 0, 219.90, N'PIX', N'Pendente', N'Pendente', @end);
    SET @ped = SCOPE_IDENTITY();

    INSERT INTO dbo.ItensPedido (PedidoId, ProdutoId, NomeProduto, Quantidade, PrecoUnitario)
    VALUES (@ped, @prod, N'Produto de Teste', 2, 100.00);

    -- baixa de estoque feita pela aplicacao na compra
    UPDATE dbo.Produtos SET QuantidadeEstoque = QuantidadeEstoque - 2 WHERE Id = @prod;   -- 10 -> 8

    /* ---------- T1: funcao de preco efetivo ---------- */
    IF dbo.fn_PrecoEfetivo(100.00, 80.00) <> 80.00 OR dbo.fn_PrecoEfetivo(100.00, 120.00) <> 100.00
       OR dbo.fn_PrecoEfetivo(100.00, NULL) <> 100.00
        THROW 51000, N'FALHA T1: fn_PrecoEfetivo retornou valor inesperado.', 1;
    PRINT N'OK  T1 fn_PrecoEfetivo';

    /* ---------- T2: pagamento confirmado finaliza o pedido e gera historico ---------- */
    INSERT INTO dbo.Pagamentos (PedidoId, FormaPagamento, Status, Valor, CodigoReferencia, Gateway)
    VALUES (@ped, N'PIX', N'Pago', 219.90, N'pix_TESTE', N'Simulado');

    IF NOT EXISTS (SELECT 1 FROM dbo.Pedidos WHERE Id = @ped AND Status = N'Finalizado' AND StatusPagamento = N'Pago')
        THROW 51000, N'FALHA T2: trg_Pagamentos_SincronizaPedido nao atualizou o pedido.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.HistoricoPagamentos
                    WHERE PedidoId = @ped AND StatusAnterior = N'Pendente' AND StatusAtual = N'Pago')
        THROW 51000, N'FALHA T2: trg_Pedidos_HistoricoPagamento nao registrou o historico.', 1;
    PRINT N'OK  T2 pagamento -> pedido e historico';

    /* ---------- T3: alerta de estoque minimo para administradores ---------- */
    UPDATE dbo.Produtos SET QuantidadeEstoque = 3 WHERE Id = @prod;                        -- 8 -> 3 (<= minimo)

    IF NOT EXISTS (SELECT 1 FROM dbo.Notificacoes WHERE UsuarioId = @adm AND Categoria = N'EstoqueBaixo')
        THROW 51000, N'FALHA T3: trg_Produtos_AlertaEstoqueMinimo nao notificou o administrador.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Produtos WHERE Id = @prod AND DataAtualizacao >= DataCadastro)
        THROW 51000, N'FALHA T3: trg_Produtos_AtualizaData nao atualizou DataAtualizacao.', 1;
    PRINT N'OK  T3 alerta de estoque minimo e DataAtualizacao';

    UPDATE dbo.Produtos SET QuantidadeEstoque = 8 WHERE Id = @prod;                        -- volta ao saldo apos a compra

    /* ---------- T4: procedure de ajuste de estoque ---------- */
    EXEC dbo.sp_AjustarEstoque @ProdutoId = @prod, @Tipo = N'Entrada', @Quantidade = 10,
                               @Motivo = N'Teste de entrada', @UsuarioId = @adm;           -- 8 -> 18

    SELECT @tmp = QuantidadeEstoque FROM dbo.Produtos WHERE Id = @prod;
    IF @tmp <> 18
        THROW 51000, N'FALHA T4: sp_AjustarEstoque nao somou a entrada.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.MovimentacoesEstoque
                    WHERE ProdutoId = @prod AND TipoMovimentacao = N'Entrada'
                      AND EstoqueAnterior = 8 AND EstoqueAtual = 18)
        THROW 51000, N'FALHA T4: movimentacao de entrada nao registrada.', 1;
    PRINT N'OK  T4 sp_AjustarEstoque';

    /* ---------- T5: relatorios executam ---------- */
    EXEC dbo.sp_RelatorioVendasPorPeriodo;
    EXEC dbo.sp_ProdutosMaisVendidos @Top = 5;
    EXEC dbo.sp_ProdutosEstoqueBaixo;
    EXEC dbo.sp_ObterResumoCliente @UsuarioId = @cli;
    PRINT N'OK  T5 relatorios e resumo do cliente';

    /* ---------- T6: cancelamento devolve estoque, estorna pagamento e notifica ---------- */
    EXEC dbo.sp_CancelarPedidoCliente @PedidoId = @ped, @UsuarioId = @cli;                 -- 18 -> 20

    SELECT @tmp = QuantidadeEstoque FROM dbo.Produtos WHERE Id = @prod;
    IF @tmp <> 20
        THROW 51000, N'FALHA T6: estoque nao foi devolvido no cancelamento.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Pagamentos WHERE PedidoId = @ped AND Status = N'Estornado')
        THROW 51000, N'FALHA T6: pagamento nao foi estornado.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Pedidos WHERE Id = @ped AND Status = N'Cancelado' AND StatusPagamento = N'Estornado')
        THROW 51000, N'FALHA T6: status do pedido/pagamento inconsistente apos cancelamento.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.MovimentacoesEstoque
                    WHERE ProdutoId = @prod AND TipoMovimentacao = N'Entrada' AND Motivo LIKE N'Estorno%')
        THROW 51000, N'FALHA T6: movimentacao de estorno nao registrada.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Notificacoes
                    WHERE UsuarioId = @cli AND Categoria = N'AtualizacoesPedidos' AND Titulo = N'Pedido cancelado')
        THROW 51000, N'FALHA T6: cliente nao foi notificado.', 1;
    PRINT N'OK  T6 cancelamento (estoque, pagamento, notificacao)';

    /* ---------- T7: pedido cancelado nao muda mais de status (por ultimo: o erro desfaz a transacao) ---------- */
    BEGIN TRY
        EXEC dbo.sp_AtualizarStatusPedido @PedidoId = @ped, @NovoStatus = N'Enviado';
        PRINT N'FALHA T7: transicao invalida foi aceita.';
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 50002
            PRINT N'OK  T7 transicao invalida rejeitada (50002)';
        ELSE
        BEGIN
            SET @txt = CONCAT(N'FALHA T7: erro inesperado ', ERROR_NUMBER(), N' - ', ERROR_MESSAGE());
            PRINT @txt;
        END
    END CATCH
END TRY
BEGIN CATCH
    PRINT CONCAT(N'FALHA: ', ERROR_MESSAGE());
END CATCH

-- Nada do teste permanece no banco.
IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
PRINT N'Transacao de teste desfeita.';
GO
