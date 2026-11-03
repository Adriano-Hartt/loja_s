using System.Globalization;
using Felibow.Mobile.Services;

namespace Felibow.Mobile;

public partial class App : Application
{
    

    public App(ApiClient api, SessionService session, SyncService sync)
    {
        InitializeComponent();
        var tema = Preferences.Default.Get("tema", "");
        if (tema == "dark") UserAppTheme = AppTheme.Dark;
        else if (tema == "light") UserAppTheme = AppTheme.Light;
        AplicarTema();
        RequestedThemeChanged += (_, _) => MainThread.BeginInvokeOnMainThread(AplicarTema);
        _ = session.CarregarAsync();

        // Token expirado/revogado em qualquer chamada => volta ao login.
        api.SessaoExpirada += () => MainThread.BeginInvokeOnMainThread(async () =>
            await Shell.Current.GoToAsync("//login"));

        // Voltou a internet: reenvia as ações feitas offline.
        Connectivity.Current.ConnectivityChanged += async (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet && session.Autenticado)
            {
                try { await sync.SincronizarPendentesAsync(); } catch { /* tenta de novo na próxima vez */ }
            }
        };
    }

    private void AplicarTema()
    {
        var escuro = RequestedTheme == AppTheme.Dark;
        void Cor(string chave, string claro, string noturno) => Resources[chave] = Color.FromArgb(escuro ? noturno : claro);
        Cor("Background", "#F7F8FC", "#0A0F22");
        Cor("Surface", "#FFFFFF", "#131A33");
        Cor("TextMain", "#202536", "#DCE1E8");
        Cor("TextMuted", "#687086", "#ACB0B9");
        Cor("Navy", "#1A2C68", "#0F1A44");
        Cor("AvisoBg", "#FFF4CC", "#3A3312");
        Resources["CardStroke"] = new SolidColorBrush(Color.FromArgb(escuro ? "#1F2849" : "#E3E7F0"));
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}

public class FavoritoTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "♥ Favorito" : "♡ Favoritar";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
