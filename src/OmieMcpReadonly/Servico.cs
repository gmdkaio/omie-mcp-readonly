using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly;

/// <summary>Everything the tools share: the companies, the fetcher, how long a tool call may wait.</summary>
public sealed class Servico(Empresas empresas, Buscador buscador, TimeSpan esperaMaxima)
{
    public const int MaximoLinhas = 200;
    public const int MaximoCaracteres = 60_000;

    public static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public Empresas Empresas => empresas;

    /// <summary>The waiting budget per tool call: OMIE_MCP_ESPERA_S, default 100 s (Claude Desktop cancels after ~4 min, 60 s on some builds).</summary>
    public static TimeSpan EsperaDoAmbiente() =>
        int.TryParse(Environment.GetEnvironmentVariable("OMIE_MCP_ESPERA_S"), out var s) && s > 0
            ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(100);

    /// <summary>Starts one query for each company (they run at once: separate keys, separate limits).</summary>
    public List<Busca> Iniciar(IReadOnlyList<Empresa> alvo, string endpoint, string metodo, JsonObject parametros, int? limite = null) =>
        alvo.Select(e => buscador.Iniciar(e, endpoint, metodo, parametros, limite)).ToList();

    /// <summary>True when every fetch finished within the budget; false means answer with <see cref="Andamento"/>.</summary>
    public Task<bool> AguardarAsync(IEnumerable<Busca> buscas, CancellationToken ct) =>
        Buscador.AguardarAsync(buscas, esperaMaxima, ct);

    /// <summary>Rows of finished fetches, each tagged with _empresa when more than one company was asked.</summary>
    public static List<JsonObject> Linhas(IReadOnlyList<Busca> buscas)
    {
        var linhas = new List<JsonObject>();
        foreach (var b in buscas)
            foreach (var l in b.Tarefa.Result)
            {
                var copia = (JsonObject)l.DeepClone();
                if (buscas.Count > 1) copia["_empresa"] = b.Empresa;
                linhas.Add(copia);
            }
        return linhas;
    }

    public static JsonObject Andamento(IEnumerable<Busca> buscas)
    {
        var lista = new JsonArray();
        double restante = 0;
        foreach (var b in buscas)
        {
            var segundos = b.Tempo.TotalSeconds;
            if (!b.Tarefa.IsCompleted && b.TotalPaginas is { } total && b.PaginasLidas > 0)
                restante = Math.Max(restante, (total - b.PaginasLidas) * segundos / b.PaginasLidas);
            lista.Add(new JsonObject
            {
                ["empresa"] = b.Empresa,
                ["pronto"] = b.Tarefa.IsCompleted,
                ["paginas_lidas"] = b.PaginasLidas,
                ["total_de_paginas"] = b.TotalPaginas,
                ["total_de_registros"] = b.TotalRegistros,
                ["segundos"] = (int)segundos,
            });
        }
        var minutos = Math.Max(1, (int)Math.Ceiling(restante / 60));
        return new JsonObject
        {
            ["situacao"] = "ainda buscando no Omie",
            ["andamento"] = lista,
            ["estimativa_restante"] = restante > 0 ? $"cerca de {minutos} min" : "desconhecida",
            ["instrucao"] = "A busca continua em segundo plano. Diga ao usuário em uma linha que ainda está buscando " +
                            "e quanto falta, e chame esta mesma ferramenta de novo com exatamente os mesmos parâmetros: " +
                            "ela continua de onde parou. Não responda com dados parciais.",
            ["avisos"] = Avisos(buscas),
        };
    }

    public static JsonArray Avisos(IEnumerable<Busca> buscas) =>
        new(buscas.SelectMany(b => b.Avisos.TakeLast(5)).Select(a => (JsonNode)a).ToArray());

    /// <summary>Keeps only the listed paths of a row, flat ("ide.dEmi": "01/02/2026"), to keep answers small.</summary>
    public static JsonObject Recortar(JsonObject linha, IReadOnlyList<string> campos)
    {
        var saida = new JsonObject();
        foreach (var c in campos)
            saida[c] = Resumo.Valor(linha, c)?.DeepClone();
        if (linha["_empresa"] is { } e) saida["_empresa"] = e.DeepClone();
        return saida;
    }

    /// <summary>Accepts parameters as an object or as a JSON string (models send both).</summary>
    public static JsonObject Parametros(JsonNode? parametros) => parametros switch
    {
        null => [],
        JsonObject o => o,
        JsonValue v when v.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s) => [],
        JsonValue v when v.TryGetValue<string>(out var s) => JsonNode.Parse(s) as JsonObject
            ?? throw new ArgumentException("parametros deve ser um objeto JSON."),
        _ => throw new ArgumentException("parametros deve ser um objeto JSON."),
    };

    public static string Data(string? texto, string nome)
    {
        if (DateOnly.TryParseExact(texto?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        throw new ArgumentException($"{nome} deve ser uma data no formato dd/mm/aaaa (recebi \"{texto}\").");
    }

    public static string Texto(JsonNode no) => no.ToJsonString(Json);
}
