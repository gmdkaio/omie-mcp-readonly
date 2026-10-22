using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly;

/// <summary>What one fetch (one company, one query) has so far.</summary>
public sealed class Busca
{
    private readonly object _trava = new();
    private readonly List<string> _avisos = [];

    public required string Empresa { get; init; }
    public required Task<IReadOnlyList<JsonObject>> Tarefa { get; init; }
    public Stopwatch Cronometro { get; } = Stopwatch.StartNew();
    public DateTimeOffset Fim { get; set; } = DateTimeOffset.MaxValue;
    public TimeSpan? Duracao { get; set; }
    public TimeSpan Tempo => Duracao ?? Cronometro.Elapsed;
    public int PaginasLidas { get; set; }
    public int? TotalPaginas { get; set; }
    public int? TotalRegistros { get; set; }

    public void Avisar(string texto) { lock (_trava) _avisos.Add(texto); }
    public IReadOnlyList<string> Avisos { get { lock (_trava) return [.. _avisos]; } }
}

/// <summary>
/// Pages through any Omie list. A fetch runs as a background job that outlives the tool call: Claude Desktop cancels
/// a tool call after a few minutes (60 s on some builds) and progress notifications don't extend it, while years of
/// data can take minutes at 0.8 to 1.2 s per page. So a tool waits a bounded time, and if the job isn't done it says
/// so; the next identical call picks up the same job. Finished jobs are kept as a cache for 15 minutes, which also
/// keeps follow-up questions clear of Omie's 60 s "consumo redundante" rule.
/// </summary>
public sealed class Buscador(IReadOnlyDictionary<string, OmieClient> clientes, TimeProvider? relogio = null)
{
    public const int TamanhoPagina = 100;
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(15);

    // endpoints that page with nPagina/nRegPorPagina; the rest use pagina/registros_por_pagina
    private static readonly HashSet<string> EstiloN =
        ["financas/mf", "financas/pesquisartitulos", "financas/contacorrentelancamentos", "financas/extrato"];

    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Busca> _buscas = new();

    public OmieClient Cliente(Empresa empresa) => clientes[empresa.Nome];

    /// <summary>
    /// Starts (or finds) the fetch of every row, or of the first <paramref name="limite"/> rows. A full fetch already
    /// running or cached also serves a limited request.
    /// </summary>
    public Busca Iniciar(Empresa empresa, string endpoint, string metodo, JsonObject parametros, int? limite = null)
    {
        endpoint = ReadOnlyGuard.NormalizarEndpoint(endpoint);
        // refuse here too, so a write never even becomes a job
        Cliente(empresa).Verificar(endpoint, metodo, parametros);

        Limpar();
        var baseChave = $"{empresa.Nome}|{endpoint}|{metodo}|{OmieClient.Canonico(parametros)}";
        if (_buscas.TryGetValue(baseChave + "|todas", out var completa) && !completa.Tarefa.IsFaulted)
            return completa;
        var chave = baseChave + (limite is null ? "|todas" : $"|{limite}");
        if (_buscas.TryGetValue(chave, out var existente) && !existente.Tarefa.IsFaulted)
            return existente;

        Busca? busca = null;
        var tarefa = new Task<Task<IReadOnlyList<JsonObject>>>(() => Paginar(busca!, empresa, endpoint, metodo, parametros, limite));
        busca = new Busca { Empresa = empresa.Nome, Tarefa = tarefa.Unwrap() };
        _buscas[chave] = busca;
        tarefa.Start(TaskScheduler.Default);
        return busca;
    }

    /// <summary>Waits for the fetch up to <paramref name="espera"/>; false means it is still running.</summary>
    public static async Task<bool> AguardarAsync(IEnumerable<Busca> buscas, TimeSpan espera, CancellationToken ct)
    {
        var todas = Task.WhenAll(buscas.Select(b => b.Tarefa));
        var vencida = await Task.WhenAny(todas, Task.Delay(espera, ct)) != todas;
        ct.ThrowIfCancellationRequested();
        if (!vencida) await todas; // surfaces the Omie error, if any
        return !vencida;
    }

    private async Task<IReadOnlyList<JsonObject>> Paginar(Busca busca, Empresa empresa, string endpoint, string metodo,
                                                          JsonObject parametros, int? limite)
    {
        try
        {
            var cliente = Cliente(empresa);
            var linhas = new List<JsonObject>();
            // Consultar/Obter return one record; only Listar/Pesquisar page
            if (!metodo.StartsWith("Listar") && !metodo.StartsWith("Pesquisar"))
            {
                var unica = await cliente.ChamarAsync(endpoint, metodo, parametros, busca.Avisar);
                busca.PaginasLidas = 1;
                if (unica is not null) linhas.Add(unica);
                busca.TotalRegistros = linhas.Count;
                return linhas;
            }

            var (chavePagina, chaveTamanho) = EstiloPaginacao(endpoint, parametros);
            var tamanho = parametros[chaveTamanho]?.GetValue<int>() ?? TamanhoPagina;
            for (var pagina = parametros[chavePagina]?.GetValue<int>() ?? 1; ; pagina++)
            {
                var p = (JsonObject)parametros.DeepClone();
                p[chavePagina] = pagina;
                p[chaveTamanho] = tamanho;
                var resposta = await cliente.ChamarAsync(endpoint, metodo, p, busca.Avisar);
                busca.PaginasLidas++;
                if (resposta is null) break;

                busca.TotalPaginas ??= Inteiro(resposta, "total_de_paginas", "nTotPaginas", "total_paginas");
                busca.TotalRegistros ??= Inteiro(resposta, "total_de_registros", "nTotRegistros");
                var lista = ListaPrincipal(resposta);
                if (lista is null)
                {
                    linhas.Add(resposta);
                    break;
                }
                linhas.AddRange(lista.OfType<JsonObject>());
                if (lista.Count == 0 || pagina >= (busca.TotalPaginas ?? pagina) || linhas.Count >= limite)
                    break;
            }
            busca.TotalRegistros ??= linhas.Count;
            return limite is null || linhas.Count <= limite ? linhas : linhas.Take(limite.Value).ToList();
        }
        finally
        {
            busca.Fim = _relogio.GetUtcNow();
            busca.Duracao = busca.Cronometro.Elapsed;
        }
    }

    /// <summary>
    /// Which parameter names carry the page number and size. Names Claude already put in the parameters win (some
    /// methods use neither convention); otherwise by endpoint.
    /// </summary>
    public static (string Pagina, string Tamanho) EstiloPaginacao(string endpoint, JsonObject parametros)
    {
        if (parametros.ContainsKey("nPagina") || parametros.ContainsKey("nRegPorPagina"))
            return ("nPagina", "nRegPorPagina");
        if (parametros.ContainsKey("pagina") || parametros.ContainsKey("registros_por_pagina"))
            return ("pagina", "registros_por_pagina");
        return EstiloN.Contains(endpoint) ? ("nPagina", "nRegPorPagina") : ("pagina", "registros_por_pagina");
    }

    /// <summary>The array of records in a list response (clientes_cadastro, movimentos, nfCadastro...): the biggest one.</summary>
    public static JsonArray? ListaPrincipal(JsonObject resposta) =>
        resposta.Select(p => p.Value).OfType<JsonArray>().OrderByDescending(a => a.Count).FirstOrDefault();

    private static int? Inteiro(JsonObject o, params string[] nomes)
    {
        foreach (var nome in nomes)
            if (o[nome] is JsonValue v && (v.TryGetValue<int>(out var i) || (v.TryGetValue<string>(out var s) && int.TryParse(s, out i))))
                return i;
        return null;
    }

    private void Limpar()
    {
        var agora = _relogio.GetUtcNow();
        foreach (var (chave, busca) in _buscas)
            if (busca.Tarefa.IsCompleted && (busca.Tarefa.IsFaulted || agora - busca.Fim > Validade))
                _buscas.TryRemove(chave, out _);
    }
}
