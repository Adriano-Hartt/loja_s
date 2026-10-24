using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class NotificacoesPage : ContentPage
{
    public NotificacoesPage(NotificacoesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((NotificacoesViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
