using Felibow.Mobile.ViewModels;

namespace Felibow.Mobile.Views;

public partial class ProdutoDetalhePage : ContentPage
{
    public ProdutoDetalhePage(ProdutoDetalheViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
