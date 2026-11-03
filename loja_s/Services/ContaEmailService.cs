namespace loja_s.Services;

public interface IContaEmailService
{
    bool IsAvailable { get; }

    Task EnviarLinkRedefinicaoAsync(string email, string link);

    Task EnviarLinkConfirmacaoAsync(string email, string link);
}

public abstract class ContaEmailException : InvalidOperationException
{
    protected ContaEmailException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

public sealed class EmailConfigurationException : ContaEmailException
{
    public EmailConfigurationException(string message) : base(message)
    {
    }
}

public sealed class EmailDeliveryException : ContaEmailException
{
    public EmailDeliveryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class ContaEmailService : IContaEmailService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ContaEmailService> _logger;
    private readonly IConfiguration _configuration;

    public ContaEmailService(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ContaEmailService> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            if (_environment.IsDevelopment())
            {
                return true;
            }

            var hasSmtpAccount = !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Username"]) &&
                                 !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Password"]);
            var noSmtpAccount = string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Username"]) &&
                                string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Password"]);
            return !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Host"]) &&
                   !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:From"]) &&
                   (hasSmtpAccount || noSmtpAccount) &&
                   HasValidPublicBaseUrl();
        }
    }

    public Task EnviarLinkRedefinicaoAsync(string email, string link) =>
        EnviarLinkAsync("redefinição de senha", email, link);

    public Task EnviarLinkConfirmacaoAsync(string email, string link) =>
        EnviarLinkAsync("confirmação de e-mail", email, link);

    private async Task EnviarLinkAsync(string finalidade, string email, string link)
    {
        if (!_environment.IsDevelopment())
        {
            var host = _configuration["Email:Smtp:Host"];
            var from = _configuration["Email:Smtp:From"];
            var port = _configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;
            if (string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(from) ||
                !IsAvailable)
            {
                throw new EmailConfigurationException(
                    "Configure o SMTP e Application__PublicBaseUrl antes de habilitar o envio de e-mail.");
            }

            using var client = new System.Net.Mail.SmtpClient(host, port)
            {
                EnableSsl = _configuration.GetValue("Email:Smtp:EnableSsl", true),
                DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new System.Net.NetworkCredential(username, password);
            }

            var safeLink = System.Net.WebUtility.HtmlEncode(link);
            using var message = new System.Net.Mail.MailMessage(from, email)
            {
                Subject = $"SupleStore — {finalidade}",
                Body = $"<p>Use o link abaixo para concluir a {finalidade}:</p><p><a href=\"{safeLink}\">Continuar</a></p><p>Se você não solicitou esta ação, ignore esta mensagem.</p>",
                IsBodyHtml = true
            };
            try
            {
                await client.SendMailAsync(message);
            }
            catch (System.Net.Mail.SmtpException exception)
            {
                throw new EmailDeliveryException("Não foi possível enviar o e-mail. Verifique a configuração SMTP e tente novamente.", exception);
            }
            return;
        }

        _logger.LogInformation("E-mail de {Finalidade} de desenvolvimento para {Email}: {Link}", finalidade, email, link);
    }

    private bool HasValidPublicBaseUrl()
    {
        var value = _configuration["Application:PublicBaseUrl"];
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment) &&
               uri.AbsolutePath == "/";
    }
}
