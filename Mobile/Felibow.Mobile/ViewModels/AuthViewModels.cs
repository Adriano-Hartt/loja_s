using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Felibow.Mobile.Models;
using Felibow.Mobile.Services;

namespace Felibow.Mobile.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly SessionService _session;

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _senha = "";
    [ObservableProperty] private string _servidor;

    private readonly SettingsService _settings;

    public LoginViewModel(ApiClient api, SessionService session, SettingsService settings)
    {
        _api = api; _session = session; _settings = settings;
        _servidor = settings.BaseUrl;
    }

    /// <summary>Reabre o app já logado se o token guardado ainda vale.</summary>
    public async Task TentarSessaoSalvaAsync()
    {
        await _session.CarregarAsync();
        if (_session.Autenticado) await Shell.Current.GoToAsync("//main");
    }

    [RelayCommand]
    private Task Entrar() => ExecutarAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Senha))
            throw new ApiException("Informe e-mail e senha.");
        _settings.BaseUrl = Servidor;
        var login = await _api.LoginAsync(Email.Trim(), Senha);
        await _session.SalvarAsync(login);
        Senha = "";
        await Shell.Current.GoToAsync("//main");
    });

    [RelayCommand]
    private Task Cadastrar()
    {
        _settings.BaseUrl = Servidor;
        return Shell.Current.GoToAsync("cadastro");
    }
}

public partial class CadastroViewModel : BaseViewModel
{
    private readonly ApiClient _api;

    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _sobrenome = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _senha = "";
    [ObservableProperty] private string _confirmarSenha = "";
    [ObservableProperty] private bool _aceitouTermos;

    public CadastroViewModel(ApiClient api) => _api = api;

    [RelayCommand]
    private Task Criar() => ExecutarAsync(async () =>
    {
        await _api.RegistrarAsync(new RegistrarRequest(Nome.Trim(), Sobrenome.Trim(), Email.Trim(),
            string.IsNullOrWhiteSpace(Telefone) ? null : Telefone.Trim(), Senha, ConfirmarSenha, AceitouTermos));
        await Shell.Current.DisplayAlert("Conta criada", "Agora é só entrar com seu e-mail e senha.", "OK");
        await Shell.Current.GoToAsync("..");
    });
}
