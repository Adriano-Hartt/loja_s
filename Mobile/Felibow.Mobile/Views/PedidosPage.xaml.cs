using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class PedidosPage : ContentPage
{
    public PedidosPage(PedidosViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((PedidosViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
