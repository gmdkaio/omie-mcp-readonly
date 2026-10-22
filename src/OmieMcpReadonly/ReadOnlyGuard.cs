using System.Text.RegularExpressions;

namespace OmieMcpReadonly;

/// <summary>
/// Decides which calls are reads. Omie keys can't be restricted (one key per company, full access to every module),
/// so this check is the only thing between the model and a write. Every call goes through <see cref="OmieClient"/>, which
/// asks this before building the request; a refused call never reaches the network.
/// </summary>
public static partial class ReadOnlyGuard
{
    /// <summary>Omie names every write Incluir, Alterar, Excluir, Upsert, Lancar...; reads use only these four.</summary>
    public static readonly string[] Prefixos = ["Listar", "Pesquisar", "Consultar", "Obter"];

    // whole name checked, not just the start: letters and digits only, prefix followed by an uppercase letter
    // ("ListarClientes"). Spaces, dots or a second JSON field smuggled into the name are refused
    [GeneratedRegex(@"^(Listar|Pesquisar|Consultar|Obter)[A-Z][A-Za-z0-9]*$")]
    private static partial Regex MetodoLeitura();

    // "geral/clientes", "financas/mf": lowercase segments only, no "..", query strings or full URLs
    [GeneratedRegex(@"^[a-z]+(/[a-z0-9]+)+$")]
    private static partial Regex EndpointValido();

    /// <summary>Why the call is refused, in Portuguese for the user, or null when it is a read.</summary>
    public static string? Recusa(string endpoint, string metodo)
    {
        if (!MetodoLeitura().IsMatch(metodo ?? ""))
            return $"Recusado: o método \"{metodo}\" não é de leitura. Este conector só faz consultas no Omie " +
                   $"(métodos que começam com {string.Join(", ", Prefixos)}) e não cria, altera nem exclui nada. " +
                   "Para mudar algo no Omie, faça direto na tela do Omie.";
        if (!EndpointValido().IsMatch(NormalizarEndpoint(endpoint)))
            return $"Recusado: \"{endpoint}\" não é um endpoint do Omie no formato grupo/serviço (ex.: geral/clientes).";
        return null;
    }

    public static string NormalizarEndpoint(string? endpoint) => (endpoint ?? "").Trim().Trim('/').ToLowerInvariant();
}

public sealed class ReadOnlyViolationException(string message) : Exception(message);
