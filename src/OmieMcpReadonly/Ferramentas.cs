using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OmieMcpReadonly;

[McpServerToolType]
public sealed class Ferramentas
{
    public const string DescricaoEmpresa =
        "Omie company name as configured (see the server instructions), or \"ambas\" for all of them combined. " +
        "If the user's question doesn't say which company, ask before calling.";

    [McpServerTool(Name = "catalogo", Title = "Catálogo do Omie", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Finds which Omie service and method answer a question, and what to send. Works offline (built from Omie's
        public docs), so it costs nothing to call.
        - busca: a few Portuguese words from the question ("estoque produto", "pedidos de venda", "contas a receber
          vencidas"). Returns the best matching services with their read methods.
        - endpoint (and optionally metodo): the exact parameter names to filter by, Omie's example call, and every
          field of the returned records as dotted paths ready for resumir (det[].prod.vProd).
        - nothing: one line per service, all of them.
        Call it before consultar or resumir whenever no recipe fits and you are not sure of the endpoint, the method
        or a parameter name. Never guess parameter names: Omie rejects unknown ones, and repeated errors block the
        company's key for 30 minutes.
        """)]
    public static Task<string> ConsultarCatalogo(
        Catalogo catalogo,
        [Description("Words from the question, in Portuguese.")] string? busca = null,
        [Description("Endpoint to detail, e.g. produtos/pedido.")] string? endpoint = null,
        [Description("Optional: one method of that endpoint to detail, e.g. ListarPedidos.")] string? metodo = null) =>
        Executar(() => Task.FromResult(
            !string.IsNullOrWhiteSpace(endpoint) ? catalogo.Detalhar(endpoint, metodo)
            : !string.IsNullOrWhiteSpace(busca) ? catalogo.Buscar(busca)
            : catalogo.Indice()));

    [McpServerTool(Name = "consultar", Title = "Consultar o Omie", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("""
        Runs any read-only Omie API method and returns up to 200 rows plus the real total. Only methods starting with
        Listar, Pesquisar, Consultar or Obter are accepted; anything else is refused before reaching Omie.
        Use it to look at records (a client, an invoice, a sample of rows) or to learn field names before calling
        resumir. Never add up or count the returned rows yourself: when the total is above the rows returned, or the
        question asks for a total, count, ranking or grouping, use resumir with the same parameters.
        Pages are fetched automatically (do not send page fields unless the method uses unusual names for them).
        Dates are text dd/mm/aaaa. Take the endpoint, method and parameter names from the catalogo tool.
        """)]
    public static Task<string> Consultar(
        Servico servico,
        [Description(DescricaoEmpresa)] string empresa,
        [Description("Omie endpoint path, e.g. geral/clientes, financas/mf, produtos/nfconsultar, produtos/pedido.")] string endpoint,
        [Description("Read-only method, e.g. ListarClientes, ListarMovimentos, ListarNF, ConsultarCliente.")] string metodo,
        [Description("The method's param object (filters), e.g. {\"filtrar_por_data_de\":\"01/01/2026\"}. Empty object when none.")] JsonNode? parametros = null,
        [Description("Optional dotted field paths to return instead of whole records, e.g. [\"ide.nNF\",\"ide.dEmi\",\"total.ICMSTot.vNF\"]. Use it for big records such as invoices.")] string[]? campos = null,
        CancellationToken ct = default) =>
        Executar(async () =>
        {
            var param = Servico.Parametros(parametros);
            var alvo = servico.Empresas.Resolver(empresa);
            var buscas = servico.Iniciar(alvo, endpoint, metodo, param, Servico.MaximoLinhas);
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento(buscas);

            var linhas = Servico.Linhas(buscas).Take(Servico.MaximoLinhas).ToList();
            if (campos is { Length: > 0 })
                linhas = linhas.Select(l => Servico.Recortar(l, campos)).ToList();
            var total = buscas.Sum(b => b.TotalRegistros ?? 0);

            var saida = new JsonObject
            {
                ["empresa"] = string.Join(" + ", buscas.Select(b => b.Empresa)),
                ["endpoint"] = endpoint,
                ["metodo"] = metodo,
                ["parametros"] = param.DeepClone(),
                ["total_de_registros"] = total,
            };
            if (buscas.Count > 1)
                saida["total_por_empresa"] = new JsonObject(buscas.Select(b => KeyValuePair.Create(b.Empresa, (JsonNode?)(b.TotalRegistros ?? 0))));

            // keep the answer inside a sane size: drop rows from the end until it fits
            var mostradas = linhas.Count;
            JsonArray Lista(int n) => new(linhas.Take(n).Select(l => (JsonNode)l.DeepClone()).ToArray());
            saida["linhas"] = Lista(mostradas);
            while (mostradas > 1 && Servico.Texto(saida).Length > Servico.MaximoCaracteres)
            {
                mostradas = Math.Max(1, mostradas * 2 / 3);
                saida["linhas"] = Lista(mostradas);
            }
            saida["linhas_devolvidas"] = mostradas;
            saida["nota"] = total > mostradas
                ? $"{total:N0} encontrados, mostrando {mostradas}" +
                  (mostradas < linhas.Count ? " (registros grandes: use campos para ver mais por vez)" : "") +
                  "; para totais, contagens ou rankings use resumir com os mesmos parâmetros."
                : $"{total:N0} encontrados, todos mostrados.";
            var avisos = Servico.Avisos(buscas);
            if (avisos.Count > 0) saida["avisos"] = avisos;
            return saida;
        });

    [McpServerTool(Name = "resumir", Title = "Resumir dados do Omie", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("""
        Same query as consultar, but reads every row and computes the answer in code: row count, sums, and grouping by
        one field with a top N. This is the tool for any total, count, average base or ranking; the numbers it returns
        are exact, so quote them instead of recomputing.
        Steps, in order: expandir (unnest an array, e.g. "det" to get one row per invoice item, parent fields kept),
        filtros (all must match), then somar and agrupar_por. Field paths are dotted, e.g. ide.dEmi, det.prod.vProd,
        detalhes.nCodCliente, resumo.nValAberto. The catalogo tool lists every field path of a method's records; a
        path with [] there (det[].prod.vProd) means expandir "det" and then the path without [] (det.prod.vProd).
        Filter operators: = != > >= < <= contem nao_contem em nao_em (valor is a list) vazio preenchido. Dates
        dd/mm/aaaa compare as dates, numbers as numbers.
        When several companies are asked, each row also has _empresa (you can group by it).
        """)]
    public static Task<string> Resumir(
        Servico servico,
        [Description(DescricaoEmpresa)] string empresa,
        [Description("Omie endpoint path, e.g. produtos/nfconsultar.")] string endpoint,
        [Description("Read-only method, e.g. ListarNF.")] string metodo,
        [Description("The method's param object (filters sent to Omie), e.g. {\"dEmiInicial\":\"01/01/2026\",\"dEmiFinal\":\"31/03/2026\",\"tpNF\":\"1\"}.")] JsonNode? parametros = null,
        [Description("Array field to unnest before filtering and summing, e.g. det.")] string? expandir = null,
        [Description("Filters applied here over every row, e.g. [{\"campo\":\"ide.finNFe\",\"operador\":\"!=\",\"valor\":\"4\"}].")] Filtro[]? filtros = null,
        [Description("Fields to add up, e.g. [\"det.prod.vProd\",\"det.prod.qCom\"].")] string[]? somar = null,
        [Description("One field to group by, e.g. det.prod.cProd.")] string? agrupar_por = null,
        [Description("dia, mes or ano when agrupar_por is a date field.")] string? periodo = null,
        [Description("Field shown as the name of each group, e.g. det.prod.xProd when grouping by product code.")] string? rotulo = null,
        [Description("contagem, grupo, or one of the somar fields. Default: first somar field, else contagem.")] string? ordenar_por = null,
        [Description("How many groups to return (default 50); the rest are summed in outros.")] int? top = null,
        CancellationToken ct = default) =>
        Executar(async () =>
        {
            var param = Servico.Parametros(parametros);
            var alvo = servico.Empresas.Resolver(empresa);
            var buscas = servico.Iniciar(alvo, endpoint, metodo, param);
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento(buscas);

            var pedido = new PedidoResumo(expandir, filtros, somar, agrupar_por, periodo, rotulo, ordenar_por, top);
            return Montar(buscas, endpoint, metodo, param, pedido);
        });

    /// <summary>The resumir answer: the summary over all rows, and when several companies, a line per company too.</summary>
    public static JsonObject Montar(List<Busca> buscas, string endpoint, string metodo, JsonObject param, PedidoResumo pedido)
    {
        var linhas = Servico.Linhas(buscas);
        var saida = new JsonObject
        {
            ["empresa"] = string.Join(" + ", buscas.Select(b => b.Empresa)),
            ["endpoint"] = endpoint,
            ["metodo"] = metodo,
            ["parametros"] = param.DeepClone(),
            ["resumo"] = Resumo.Resumir(linhas, pedido),
        };
        if (buscas.Count > 1)
        {
            var porEmpresa = new JsonObject();
            foreach (var b in buscas)
            {
                var r = Resumo.Resumir(b.Tarefa.Result, pedido with { AgruparPor = null });
                porEmpresa[b.Empresa] = r;
            }
            saida["por_empresa"] = porEmpresa;
        }
        var avisos = Servico.Avisos(buscas);
        if (avisos.Count > 0) saida["avisos"] = avisos;
        saida["tempo_de_busca_s"] = (int)buscas.Max(b => b.Tempo.TotalSeconds);
        return saida;
    }

    /// <summary>
    /// Errors the user should hear about (refused write, wrong company, Omie fault) go back as tool errors with the
    /// message intact; McpException is what the SDK passes through as text.
    /// </summary>
    public static async Task<string> Executar(Func<Task<JsonObject>> acao)
    {
        try
        {
            return Servico.Texto(await acao());
        }
        catch (Exception e) when (e is ReadOnlyViolationException or ArgumentException or OmieException or FormatException
                                      or System.Text.Json.JsonException or InvalidOperationException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
