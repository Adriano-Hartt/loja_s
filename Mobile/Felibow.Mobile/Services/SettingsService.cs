namespace Felibow.Mobile.Services;

/// <summary>URL base da API (configurável na tela Conta).</summary>
public class SettingsService
{
    private const string Chave = "api_base_url_v3";

    // 10.0.2.2 = máquina host vista pelo emulador Android.
    public string DefaultUrl => "https://felibow.onrender.com";

    public string BaseUrl
    {
        get => Preferences.Default.Get(Chave, DefaultUrl);
        set => Preferences.Default.Set(Chave, Normalizar(value));
    }

    private static string Normalizar(string url) => string.IsNullOrWhiteSpace(url) ? "" : url.Trim().TrimEnd('/');
}
