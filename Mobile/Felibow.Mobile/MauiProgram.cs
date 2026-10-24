using Felibow.Mobile.Services;
using Felibow.Mobile.ViewModels;
using Felibow.Mobile.Views;
using Microsoft.Extensions.Logging;

namespace Felibow.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<ApiClient>();
        builder.Services.AddSingleton<LocalDatabase>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddTransient<LoginViewModel>().AddTransient<LoginPage>();
        builder.Services.AddTransient<CadastroViewModel>().AddTransient<CadastroPage>();
        builder.Services.AddTransient<CatalogoViewModel>().AddTransient<CatalogoPage>();
        builder.Services.AddTransient<ProdutoDetalheViewModel>().AddTransient<ProdutoDetalhePage>();
        builder.Services.AddTransient<CarrinhoViewModel>().AddTransient<CarrinhoPage>();
        builder.Services.AddTransient<CheckoutViewModel>().AddTransient<CheckoutPage>();
        builder.Services.AddTransient<PedidosViewModel>().AddTransient<PedidosPage>();
        builder.Services.AddTransient<PedidoDetalheViewModel>().AddTransient<PedidoDetalhePage>();
        builder.Services.AddTransient<FavoritosViewModel>().AddTransient<FavoritosPage>();
        builder.Services.AddTransient<NotificacoesViewModel>().AddTransient<NotificacoesPage>();
        builder.Services.AddTransient<ContaViewModel>().AddTransient<ContaPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
