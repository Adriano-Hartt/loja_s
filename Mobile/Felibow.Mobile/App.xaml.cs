using System.Globalization;
using Felibow.Mobile.Services;

namespace Felibow.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell, ApiClient api, SessionService session, SyncService sync)
    {
        InitializeComponent();
        _shell = shell;
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

    protected override Window CreateWindow(IActivationState? activationState) => new(_shell);
}

public class FavoritoTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "♥ Favorito" : "♡ Favoritar";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
