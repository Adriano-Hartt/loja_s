using Felibow.Mobile.Models;

namespace Felibow.Mobile.Services;

/// <summary>Guarda o JWT no armazenamento seguro do Android (Keystore).</summary>
public class SessionService
{
    private const string ChaveToken = "jwt";
    private const string ChaveExpira = "jwt_exp";

    public string? Token { get; private set; }
    public DateTime? ExpiraEm { get; private set; }
    public UsuarioDto? Usuario { get; set; }
    public bool Autenticado => !string.IsNullOrEmpty(Token) && (ExpiraEm ?? DateTime.MinValue) > DateTime.UtcNow;

    public async Task CarregarAsync()
    {
        try
        {
            Token = await SecureStorage.Default.GetAsync(ChaveToken);
            var exp = await SecureStorage.Default.GetAsync(ChaveExpira);
            ExpiraEm = DateTime.TryParse(exp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }
        catch
        {
            Token = null;
        }
    }

    public async Task SalvarAsync(LoginResponse login)
    {
        Token = login.AccessToken;
        ExpiraEm = login.ExpiraEm;
        Usuario = login.Usuario;
        await SecureStorage.Default.SetAsync(ChaveToken, login.AccessToken);
        await SecureStorage.Default.SetAsync(ChaveExpira, login.ExpiraEm.ToString("O"));
    }

    public void Limpar()
    {
        Token = null; ExpiraEm = null; Usuario = null;
        SecureStorage.Default.Remove(ChaveToken);
        SecureStorage.Default.Remove(ChaveExpira);
    }
}
