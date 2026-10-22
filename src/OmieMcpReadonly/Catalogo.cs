using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly;

public sealed record CampoOmie(string Nome, string Tipo, string Descricao, bool Obrigatorio, string? Ref);
public sealed record TipoOmie(string Descricao, List<CampoOmie> Campos, string? Elemento);
public sealed record MetodoOmie(string Nome, string Descricao, string Requisicao, string Resposta, string Exemplo);
public sealed record ServicoOmie(string Endpoint, string Grupo, string Titulo, string Descricao, List<MetodoOmie> Metodos,
                                 List<string> Recusados, Dictionary<string, TipoOmie> Tipos);
public sealed record CatalogoOmie(string GeradoEm, string Fonte, List<ServicoOmie> Servicos);

/// <summary>
/// Every Omie service and the read methods the connector accepts, with each method's parameters and record fields,
/// taken from Omie's public docs by build/catalogo-omie.ps1 and built into the exe. It lets Claude find the service
/// for a question and send the exact parameter names: Omie answers an unknown parameter with an error, and 10 errors
/// on the same key + method block it for 30 minutes.
/// </summary>
public sealed partial class Catalogo
{
    private const int MaximoCampos = 400;
    private const int ProfundidadeMaxima = 5;
    private static readonly HashSet<string> Vazias =
    [
        "que", "qual", "quais", "quanto", "quantos", "quantas", "para", "por", "com", "dos", "das", "nos", "nas", "uma",
        "mais", "menos", "meu", "minha", "nosso", "nossa", "todos", "todas", "cada", "esse", "essa", "este", "esta", "omie",
        "temos", "tivemos", "tem", "estamos", "foram", "foi",
    ];

    public CatalogoOmie Dados { get; }

    // the searchable text of each service, normalized once: name, description, methods, field descriptions
    private readonly List<(ServicoOmie Servico, string Nome, string Descricao, string Metodos, string Campos)> _textos;

    public Catalogo(CatalogoOmie dados)
    {
        Dados = dados;
        _textos = dados.Servicos.Select(s => (s,
            Normalizar($"{s.Endpoint.Replace('/', ' ')} {s.Titulo} {s.Grupo}"),
            Normalizar(s.Descricao),
            Normalizar(string.Join(" ", s.Metodos.Select(m => $"{SepararPalavras(m.Nome)} {m.Descricao}"))),
            Normalizar(string.Join(" ", s.Tipos.Values.SelectMany(t => t.Campos).Select(c => c.Descricao))))).ToList();
    }

    public static Catalogo Embutido()
    {
        using var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalogo-omie.json")
                          ?? throw new InvalidOperationException("catálogo do Omie não embutido no executável");
        var opcoes = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        return new Catalogo(JsonSerializer.Deserialize<CatalogoOmie>(fluxo, opcoes)!);
    }

    /// <summary>
    /// The page-number and page-size fields of a method's request, from its docs. Omie uses at least five spellings
    /// (pagina/registros_por_pagina, nPagina/nRegPorPagina, nRegistrosPorPagina, nRegsPorPagina, nPagina alone), and
    /// some list methods don't page at all. Null when the method isn't in the catalog.
    /// </summary>
    public (string? Pagina, string? Tamanho)? Paginacao(string endpoint, string metodo)
    {
        var ep = ReadOnlyGuard.NormalizarEndpoint(endpoint);
        var servico = Dados.Servicos.FirstOrDefault(s => s.Endpoint == ep);
        var m = servico?.Metodos.FirstOrDefault(x => x.Nome == metodo);
        if (servico is null || m is null || !servico.Tipos.TryGetValue(m.Requisicao, out var requisicao))
            return null;
        var nomes = requisicao.Campos.Select(c => c.Nome).ToList();
        return (nomes.FirstOrDefault(n => PaginaRegex().IsMatch(n)), nomes.FirstOrDefault(n => TamanhoRegex().IsMatch(n)));
    }

    [System.Text.RegularExpressions.GeneratedRegex("^n?[Pp]agina$")]
    private static partial System.Text.RegularExpressions.Regex PaginaRegex();

    [System.Text.RegularExpressions.GeneratedRegex("(?i)^n?reg.*pag")]
    private static partial System.Text.RegularExpressions.Regex TamanhoRegex();

    /// <summary>One line per service: endpoint, name and the read methods.</summary>
    public JsonObject Indice() => new()
    {
        ["fonte"] = $"documentação pública do Omie, lida em {Dados.GeradoEm}",
        ["servicos"] = new JsonArray(Dados.Servicos.Select(s => (JsonNode)new JsonObject
        {
            ["endpoint"] = s.Endpoint,
            ["servico"] = $"{s.Grupo}: {s.Titulo}",
            ["metodos"] = s.Metodos.Count == 0 ? "nenhum (só escrita)" : string.Join(", ", s.Metodos.Select(m => m.Nome)),
        }).ToArray()),
    };

    /// <summary>Services ranked by how well their names, methods and fields match the words of a question.</summary>
    public JsonObject Buscar(string busca, int maximo = 12)
    {
        var palavras = Normalizar(busca).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 3 && !Vazias.Contains(p))
            // "vencidos" should find "vencido": compare on the stem
            .Select(p => p.Length > 4 && p.EndsWith('s') ? p[..^1] : p)
            .Distinct().ToList();
        if (palavras.Count == 0)
            return Indice();

        var achados = _textos
            .Select(t => (t.Servico, Pontos: Pontuar(t, palavras)))
            .Where(x => x.Pontos > 0)
            .OrderByDescending(x => x.Pontos).ThenBy(x => x.Servico.Endpoint, StringComparer.Ordinal)
            .Take(maximo)
            .Select(x => (JsonNode)new JsonObject
            {
                ["endpoint"] = x.Servico.Endpoint,
                ["servico"] = $"{x.Servico.Grupo}: {x.Servico.Titulo}",
                ["descricao"] = x.Servico.Descricao,
                ["metodos"] = new JsonArray(x.Servico.Metodos.Select(m => (JsonNode)(m.Descricao.Length > 0 ? $"{m.Nome}: {m.Descricao}" : m.Nome)).ToArray()),
            })
            .ToArray();
        return new JsonObject
        {
            ["busca"] = string.Join(" ", palavras),
            ["servicos"] = new JsonArray(achados),
            ["proximo_passo"] = achados.Length == 0
                ? "Nada encontrado com essas palavras. Tente outras, ou chame catalogo sem argumentos para ver todos os serviços."
                : "Chame catalogo com o endpoint escolhido (e o método) para ver os parâmetros exatos e os campos dos registros antes de consultar ou resumir.",
        };
    }

    /// <summary>
    /// Everything needed to call a service: each read method's example, its parameters, and the fields of the records
    /// it returns as dotted paths (det[].prod.vProd), which is the form resumir takes.
    /// </summary>
    public JsonObject Detalhar(string endpoint, string? metodo = null)
    {
        var ep = ReadOnlyGuard.NormalizarEndpoint(endpoint);
        var servico = Dados.Servicos.FirstOrDefault(s => s.Endpoint == ep)
            ?? throw new ArgumentException($"O endpoint \"{endpoint}\" não está no catálogo do Omie. Use catalogo com busca para achar o serviço certo.");

        var metodos = servico.Metodos;
        if (!string.IsNullOrWhiteSpace(metodo))
        {
            var nome = metodo.Trim();
            metodos = metodos.Where(m => string.Equals(m.Nome, nome, StringComparison.OrdinalIgnoreCase)).ToList();
            if (metodos.Count == 0)
                throw new ArgumentException(servico.Recusados.Contains(nome, StringComparer.OrdinalIgnoreCase)
                    ? $"{nome} existe em {ep}, mas não é de leitura e o conector recusa. Métodos de leitura deste serviço: {Nomes(servico)}."
                    : $"{nome} não existe em {ep}. Métodos de leitura deste serviço: {Nomes(servico)}.");
        }

        return new JsonObject
        {
            ["endpoint"] = servico.Endpoint,
            ["servico"] = $"{servico.Grupo}: {servico.Titulo}",
            ["descricao"] = servico.Descricao,
            ["documentacao"] = $"https://app.omie.com.br/api/v1/{servico.Endpoint}/",
            ["metodos"] = new JsonArray(metodos.Select(m => (JsonNode)Metodo(servico, m)).ToArray()),
            ["recusados_por_nao_serem_leitura"] = string.Join(", ", servico.Recusados),
            ["como_usar"] = "Mande em parametros só os filtros (nomes exatos da lista parametros; datas dd/mm/aaaa). " +
                            "Não mande campos de página: consultar e resumir paginam sozinhos. Em resumir, use os caminhos de " +
                            "campos_do_registro; um caminho com [] é uma lista: use expandir com a parte antes do [] (ex.: det) " +
                            "e escreva o caminho sem os [] (det.prod.vProd).",
        };
    }

    private JsonObject Metodo(ServicoOmie s, MetodoOmie m)
    {
        var saida = new JsonObject
        {
            ["metodo"] = m.Nome,
            ["descricao"] = m.Descricao,
            ["exemplo_do_omie"] = m.Exemplo,
            ["parametros"] = Linhas(Achatar(s, m.Requisicao, ignorar: ["pagina", "registros_por_pagina", "nPagina", "nRegPorPagina"])),
        };
        // a list response has paging fields plus one array of records; describe the record, not the envelope
        if (s.Tipos.GetValueOrDefault(m.Resposta) is { } resposta
            && resposta.Campos.FirstOrDefault(c => c.Ref is { } r && s.Tipos.GetValueOrDefault(r)?.Elemento is not null) is { } lista)
        {
            saida["lista_de_registros"] = lista.Nome;
            saida["total_vem_em"] = string.Join(", ", resposta.Campos.Where(c => c.Nome.StartsWith("total") || c.Nome.StartsWith("nTot")).Select(c => c.Nome));
            saida["campos_do_registro"] = Linhas(Achatar(s, s.Tipos[lista.Ref!].Elemento!));
        }
        else
        {
            saida["campos_do_registro"] = Linhas(Achatar(s, m.Resposta));
        }
        return saida;
    }

    private static JsonArray Linhas(List<string> linhas) => new(linhas.Select(l => (JsonNode)l).ToArray());

    /// <summary>A type's leaf fields as "caminho (tipo): descrição", following nested types and lists.</summary>
    private static List<string> Achatar(ServicoOmie s, string tipo, IReadOnlyCollection<string>? ignorar = null)
    {
        var linhas = new List<string>();
        void Visitar(string nomeTipo, string prefixo, int profundidade, HashSet<string> caminho)
        {
            if (!s.Tipos.TryGetValue(nomeTipo, out var t) || !caminho.Add(nomeTipo)) return;
            foreach (var c in t.Campos.Where(c => c.Nome.Length > 0))
            {
                if (linhas.Count >= MaximoCampos) return;
                if (prefixo.Length == 0 && ignorar?.Contains(c.Nome) == true) continue;
                var destino = c.Ref is null ? null : s.Tipos.GetValueOrDefault(c.Ref);
                var elemento = destino?.Elemento;
                var filho = elemento ?? c.Ref;
                if (filho is not null && s.Tipos.ContainsKey(filho) && profundidade < ProfundidadeMaxima)
                    Visitar(filho, $"{prefixo}{c.Nome}{(elemento is null ? "" : "[]")}.", profundidade + 1, caminho);
                else
                    linhas.Add($"{prefixo}{c.Nome} ({c.Tipo}{(c.Obrigatorio ? ", obrigatório" : "")}){(c.Descricao.Length > 0 ? ": " + Recortar(c.Descricao, 140) : "")}");
            }
            caminho.Remove(nomeTipo);
        }
        Visitar(tipo, "", 0, []);
        if (linhas.Count >= MaximoCampos)
            linhas.Add($"(lista cortada em {MaximoCampos} campos)");
        return linhas;
    }

    private static int Pontuar((ServicoOmie Servico, string Nome, string Descricao, string Metodos, string Campos) t, List<string> palavras)
    {
        // a service with no read method can't answer anything
        if (t.Servico.Metodos.Count == 0) return 0;
        var pontos = 0;
        foreach (var p in palavras)
        {
            if (t.Nome.Contains(p)) pontos += 6;
            if (t.Descricao.Contains(p)) pontos += 3;
            if (t.Metodos.Contains(p)) pontos += 3;
            if (t.Campos.Contains(p)) pontos += 1;
        }
        return pontos;
    }

    private static string Nomes(ServicoOmie s) => s.Metodos.Count == 0 ? "nenhum" : string.Join(", ", s.Metodos.Select(m => m.Nome));

    // "ListarContasReceber" -> "Listar Contas Receber", so a search for "receber" finds it
    private static string SepararPalavras(string nome) =>
        string.Concat(nome.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + ch : ch.ToString()));

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..max] + "…";

    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder();
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return sb.ToString();
    }
}
