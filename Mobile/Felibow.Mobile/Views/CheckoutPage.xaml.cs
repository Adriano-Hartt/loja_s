using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class CheckoutPage : ContentPage
{
    public CheckoutPage(CheckoutViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((CheckoutViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
