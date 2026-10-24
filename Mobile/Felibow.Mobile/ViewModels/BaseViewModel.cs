using CommunityToolkit.Mvvm.ComponentModel;
using Felibow.Mobile.Services;

namespace Felibow.Mobile.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NaoOcupado))] private bool _ocupado;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TemErro))] private string _erro = "";

    public bool NaoOcupado => !Ocupado;
    public bool TemErro => !string.IsNullOrEmpty(Erro);

    /// <summary>Executa a ação mostrando indicador e convertendo falhas da API em mensagem.</summary>
    protected async Task ExecutarAsync(Func<Task> acao)
    {
        if (Ocupado) return;
        Ocupado = true;
        Erro = "";
        try { await acao(); }
        catch (ApiException ex) { Erro = ex.Message; }
        catch (Exception) { Erro = "Algo deu errado. Tente novamente."; }
        finally { Ocupado = false; }
    }

    protected static string Moeda(decimal v) => v.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
}
