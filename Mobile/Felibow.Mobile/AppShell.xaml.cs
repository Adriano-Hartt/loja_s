using Felibow.Mobile.Views;

namespace Felibow.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("cadastro", typeof(CadastroPage));
        Routing.RegisterRoute("produto", typeof(ProdutoDetalhePage));
        Routing.RegisterRoute("checkout", typeof(CheckoutPage));
        Routing.RegisterRoute("pedido", typeof(PedidoDetalhePage));
        Routing.RegisterRoute("favoritos", typeof(FavoritosPage));
        Routing.RegisterRoute("notificacoes", typeof(NotificacoesPage));
    }
}
