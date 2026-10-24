# Felibow Mobile (.NET MAUI – Android)

App cliente da loja, integrado à API REST do `loja_s` (`/api/v1`, JWT).

## Telas e fluxo
Login → (Cadastro) → Abas: **Catálogo** → Detalhe do produto → **Carrinho** → **Checkout** → **Pedidos** → Detalhe do pedido (copiar código, cancelar) · **Conta** (resumo, Favoritos, Notificações, servidor, sair).

## Requisitos de manual atendidos
- Autenticação: JWT guardado em `SecureStorage`; 401 encerra a sessão e volta ao login.
- Integração com API: `Services/ApiClient.cs` (Bearer, erros ProblemDetails).
- Sincronização: catálogo gravado em SQLite local (`LocalDatabase`) e exibido offline com aviso (`SyncService`). Compras exigem conexão.
- Acessibilidade: `SemanticProperties` nos controles principais.

## Executar
1. Suba a API: `cd loja_s && dotnet run --launch-profile http` (porta 5258).
2. Workload: `dotnet workload install maui-android`.
3. `cd Mobile/Felibow.Mobile && dotnet build -t:Run -f net10.0-android` (emulador Android ligado).
4. No emulador o servidor padrão é `http://10.0.2.2:5258`. Em aparelho real, use o IP da máquina na tela de login/Conta.

Cleartext HTTP está liberado no manifesto **apenas para desenvolvimento**; em produção use HTTPS e remova `usesCleartextTraffic`.
