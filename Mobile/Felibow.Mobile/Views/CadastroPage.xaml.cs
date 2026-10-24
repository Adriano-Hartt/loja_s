using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class CadastroPage : ContentPage
{
    public CadastroPage(CadastroViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
