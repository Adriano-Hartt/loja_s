using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class CatalogoPage : ContentPage
{
    public CatalogoPage(CatalogoViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((CatalogoViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
