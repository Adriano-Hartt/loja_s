using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace loja_s.Data;

/// <summary>
/// Erro de regra de negócio levantado por uma stored procedure/trigger com THROW 5000x
/// (ver convenção em Database/03_procedures.sql).
/// </summary>
public sealed class ProcedureException : Exception
{
    public ProcedureException(int numero, string message) : base(message)
    {
        Numero = numero;
    }

    /// <summary>50001 não encontrado, 50002 regra violada, 50003 não permitido, 50004 parâmetro inválido.</summary>
    public int Numero { get; }
}

public static class DbProcedures
{
    /// <summary>
    /// Executa uma stored procedure do SQL Server e devolve o primeiro conjunto de resultados
    /// como lista de linhas (nome da coluna -> valor).
    /// </summary>
    public static async Task<List<Dictionary<string, object?>>> QueryProcedureAsync(
        this DbContext context,
        string procedure,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        var abriuAqui = connection.State != ConnectionState.Open;
        if (abriuAqui)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = procedure;
            command.CommandType = CommandType.StoredProcedure;

            if (parameters != null)
            {
                foreach (var (nome, valor) in parameters)
                {
                    var parametro = command.CreateParameter();
                    parametro.ParameterName = "@" + nome;
                    parametro.Value = valor ?? DBNull.Value;
                    command.Parameters.Add(parametro);
                }
            }

            var linhas = new List<Dictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var linha = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    linha[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken)
                        ? null
                        : reader.GetValue(i);
                }

                linhas.Add(linha);
            }

            return linhas;
        }
        catch (SqlException ex) when (ex.Number is >= 50001 and <= 50004)
        {
            throw new ProcedureException(ex.Number, ex.Message);
        }
        finally
        {
            if (abriuAqui)
            {
                await connection.CloseAsync();
            }
        }
    }
}
