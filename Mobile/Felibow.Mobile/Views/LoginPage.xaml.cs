using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((LoginViewModel)BindingContext).TentarSessaoSalvaAsync();
    }
}
