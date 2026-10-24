using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class ContaPage : ContentPage
{
    public ContaPage(ContaViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((ContaViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
