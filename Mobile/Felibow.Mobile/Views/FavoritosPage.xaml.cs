using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class FavoritosPage : ContentPage
{
    public FavoritosPage(FavoritosViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (((FavoritosViewModel)BindingContext).CarregarCommand).Execute(null);
    }
}
