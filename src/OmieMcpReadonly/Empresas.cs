using System.Collections;

namespace OmieMcpReadonly;

public sealed record Empresa(string Nome, string AppKey, string AppSecret);

/// <summary>
/// The companies this install can read, from environment variables set by the .mcpb manifest:
/// OMIE_EMPRESA_1_NOME, OMIE_EMPRESA_1_APP_KEY, OMIE_EMPRESA_1_APP_SECRET, then _2_, up to _9_.
/// </summary>
public sealed class Empresas
{
    public const int Maximo = 9;
    public static readonly string[] PalavrasTodas = ["ambas", "todas", "as duas", "both", "all"];

    public IReadOnlyList<Empresa> Lista { get; }

    public Empresas(IEnumerable<Empresa> lista) => Lista = lista.ToList();

    public static Empresas DoAmbiente() => DoAmbiente(Environment.GetEnvironmentVariables());

    public static Empresas DoAmbiente(IDictionary env)
    {
        string? Ler(string nome)
        {
            var valor = env[nome] as string;
            // an optional user_config field left blank can reach us as "" or as the literal "${user_config.x}"
            return string.IsNullOrWhiteSpace(valor) || valor.Contains("${") ? null : valor.Trim();
        }

        var lista = new List<Empresa>();
        for (var n = 1; n <= Maximo; n++)
        {
            var nome = Ler($"OMIE_EMPRESA_{n}_NOME");
            var key = Ler($"OMIE_EMPRESA_{n}_APP_KEY");
            var secret = Ler($"OMIE_EMPRESA_{n}_APP_SECRET");
            if (nome is not null && key is not null && secret is not null)
                lista.Add(new Empresa(nome.ToUpperInvariant(), key, secret));
        }
        return new Empresas(lista);
    }

    public string Nomes => string.Join(", ", Lista.Select(e => e.Nome));

    /// <summary>
    /// The companies a question is about. "ambas"/"todas" means every configured one. A blank name with more than
    /// one company is an error on purpose: the instructions tell Claude to ask the user instead of guessing.
    /// </summary>
    public IReadOnlyList<Empresa> Resolver(string? empresa)
    {
        if (Lista.Count == 0)
            throw new ArgumentException("Nenhuma empresa do Omie está configurada neste conector. " +
                                        "Confira as chaves nas configurações da extensão no Claude Desktop.");
        var pedido = (empresa ?? "").Trim();
        if (pedido.Length == 0)
        {
            if (Lista.Count == 1) return Lista;
            throw new ArgumentException($"Falta dizer a empresa. Pergunte ao usuário qual delas: {Nomes}, ou ambas.");
        }
        if (PalavrasTodas.Contains(pedido.ToLowerInvariant()))
            return Lista;
        var achada = Lista.FirstOrDefault(e => string.Equals(e.Nome, pedido, StringComparison.OrdinalIgnoreCase));
        return achada is not null
            ? [achada]
            : throw new ArgumentException($"Empresa \"{pedido}\" não encontrada. As empresas configuradas são: {Nomes} (ou ambas).");
    }
}
