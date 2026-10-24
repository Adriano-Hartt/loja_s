
## Sincronização offline
Favoritar/desfavoritar e adicionar ao carrinho funcionam sem internet: a ação vai para a tabela `OperacaoPendente` (SQLite do aparelho) e é reenviada em ordem quando a conexão volta (evento de conectividade, abertura do catálogo). Ações recusadas pelo servidor (ex.: sem estoque) são descartadas; ao sair da conta a fila é apagada. Finalizar compra e cancelar pedido exigem conexão.
