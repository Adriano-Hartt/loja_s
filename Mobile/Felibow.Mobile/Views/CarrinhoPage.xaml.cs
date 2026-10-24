using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class CarrinhoPage : ContentPage
{
    public CarrinhoPage(CarrinhoViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((CarrinhoViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
