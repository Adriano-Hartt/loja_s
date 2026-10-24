using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class PedidoDetalhePage : ContentPage
{
    public PedidoDetalhePage(PedidoDetalheViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
