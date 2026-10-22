using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OmieMcpReadonly;

/// <summary>
/// Omie API client for one company (one app_key). Ported from an earlier extraction client, where every rule below
/// was learned on 20 and 21/10/2026: one call at a time per key + method, the 60 s "consumo redundante" window, the
/// empty-result lock, and which errors are worth retrying.
/// </summary>
public sealed partial class OmieClient
{
    public const string BaseUrl = "https://app.omie.com.br/api/v1/";
    public const int Tentativas = 4;

    /// <summary>Waits used by the retry rules. Tests swap these for zero.</summary>
    public sealed record Esperas(TimeSpan MetodoOcupado, TimeSpan JanelaRedundante, TimeSpan Folga,
                                 Func<int, TimeSpan> Transitorio, Func<int, TimeSpan> Rede)
    {
        public static readonly Esperas Padrao = new(
            MetodoOcupado: TimeSpan.FromSeconds(62),
            JanelaRedundante: TimeSpan.FromSeconds(60),
            Folga: TimeSpan.FromSeconds(3),
            Transitorio: t => TimeSpan.FromSeconds(5 * Math.Pow(2, t)),
            Rede: t => TimeSpan.FromSeconds(15 * Math.Pow(2, t)));
    }

    private readonly HttpClient _http;
    private readonly CallLog _log;
    private readonly Esperas _esperas;
    private readonly TimeProvider _relogio;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar;
    // one call at a time per method on this key: ListarMovimentos answers a second one with "Já existe uma
    // requisição desse método sendo executada". Different methods on the same key can run at once
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _vagas = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _ultimaChamada = new();

    public Empresa Empresa { get; }

    public OmieClient(Empresa empresa, HttpClient http, CallLog log, Esperas? esperas = null,
                      TimeProvider? relogio = null, Func<TimeSpan, CancellationToken, Task>? esperar = null)
    {
        Empresa = empresa;
        _http = http;
        _log = log;
        _esperas = esperas ?? Esperas.Padrao;
        _relogio = relogio ?? TimeProvider.System;
        _esperar = esperar ?? Task.Delay;
    }

    /// <summary>
    /// One call. Returns the response object, or null when Omie says there are no records (it answers an empty
    /// page with a fault instead of an empty list). <paramref name="aviso"/> gets a line for every wait, so a
    /// long answer doesn't go quiet.
    /// </summary>
    public async Task<JsonObject?> ChamarAsync(string endpoint, string metodo, JsonObject param,
                                               Action<string>? aviso = null, CancellationToken ct = default)
    {
        endpoint = ReadOnlyGuard.NormalizarEndpoint(endpoint);
        Verificar(endpoint, metodo, param);

        var corpo = new JsonObject
        {
            ["call"] = metodo,
            ["app_key"] = Empresa.AppKey,
            ["app_secret"] = Empresa.AppSecret,
            ["param"] = new JsonArray(param.DeepClone()),
        }.ToJsonString();

        var assinatura = $"{endpoint}|{metodo}|{Canonico(param)}";
        var vaga = _vagas.GetOrAdd(metodo, _ => new SemaphoreSlim(1, 1));
        await vaga.WaitAsync(ct);
        var cronometro = Stopwatch.StartNew();
        try
        {
            // Omie only answers the first of two identical calls within 60 s
            if (_ultimaChamada.TryGetValue(assinatura, out var ultima))
            {
                var falta = _esperas.JanelaRedundante - (_relogio.GetUtcNow() - ultima);
                if (falta > TimeSpan.Zero)
                    await _esperar(falta, ct);
            }
            var resposta = await EnviarAsync(endpoint, corpo, assinatura, aviso, ct);
            var linhas = resposta is null ? 0 : ContarLinhas(resposta);
            _log.Registrar(Empresa.Nome, endpoint, metodo, param, resposta is null ? "vazio" : "ok", linhas, cronometro.Elapsed);
            return resposta;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.Registrar(Empresa.Nome, endpoint, metodo, param, "erro: " + e.Message, null, cronometro.Elapsed);
            throw;
        }
        finally
        {
            vaga.Release();
        }
    }

    /// <summary>Throws (and logs the refusal) when the call is not a read.</summary>
    public void Verificar(string endpoint, string metodo, JsonObject param)
    {
        if (ReadOnlyGuard.Recusa(endpoint, metodo) is not { } recusa) return;
        _log.Registrar(Empresa.Nome, ReadOnlyGuard.NormalizarEndpoint(endpoint), metodo, param, "recusado", null, TimeSpan.Zero);
        throw new ReadOnlyViolationException(recusa);
    }

    private async Task<JsonObject?> EnviarAsync(string endpoint, string corpo, string assinatura,
                                                Action<string>? aviso, CancellationToken ct)
    {
        var ultimo = "";
        for (var tentativa = 0; tentativa < Tentativas; tentativa++)
        {
            _ultimaChamada[assinatura] = _relogio.GetUtcNow();
            HttpResponseMessage http;
            string texto;
            try
            {
                var conteudo = new StringContent(corpo, Encoding.UTF8);
                // exactly "application/json": with "; charset=utf-8" (the .NET default) Omie answers every call with
                // a SOAP "Bad Request" envelope (22/10/2026)
                conteudo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                using var pedido = new HttpRequestMessage(HttpMethod.Post, BaseUrl + endpoint + "/") { Content = conteudo };
                http = await _http.SendAsync(pedido, ct);
                texto = await http.Content.ReadAsStringAsync(ct);
            }
            catch (Exception e) when (e is HttpRequestException or IOException
                                          || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // dropped connection or timeout: the call may have counted on Omie's side, so the retry can hit
                // "consumo redundante"; that branch then waits the time Omie asks for
                ultimo = $"falha de rede ao chamar o Omie: {e.Message}";
                if (tentativa == Tentativas - 1)
                    throw new OmieException(-1, ultimo);
                var espera = _esperas.Rede(tentativa);
                Avisar(aviso, $"a conexão com o Omie caiu, tentando de novo em {espera.TotalSeconds:0} s");
                await _esperar(espera, ct);
                continue;
            }

            JsonObject? dados = null;
            try { dados = JsonNode.Parse(texto) as JsonObject; } catch (JsonException) { }
            var falha = dados?["faultstring"]?.ToString();

            if (http.IsSuccessStatusCode && dados is not null && falha is null)
                return (JsonObject)DesfazerEntidades(dados)!;

            falha ??= dados is null ? Recortar(texto, 300) : $"HTTP {(int)http.StatusCode}";
            ultimo = falha;

            if (falha.Contains("Não existem registros", StringComparison.OrdinalIgnoreCase))
                return null;
            if (falha.Contains("sendo executada", StringComparison.OrdinalIgnoreCase))
            {
                // still busy on Omie's side (an empty result leaves it busy for 15 s or more). A quick retry is
                // refused again and then counts as redundant, so wait the whole 60 s window once
                Avisar(aviso, $"o Omie ainda está processando a chamada anterior, esperando {_esperas.MetodoOcupado.TotalSeconds:0} s");
                await _esperar(_esperas.MetodoOcupado, ct);
                continue;
            }
            if (falha.Contains("consumo redundante", StringComparison.OrdinalIgnoreCase) || falha.Contains("REDUNDANT"))
            {
                // "Aguarde 47 segundos": any retry inside that window restarts Omie's clock, so wait it out
                var m = Segundos().Match(falha);
                var espera = (m.Success ? TimeSpan.FromSeconds(int.Parse(m.Groups[1].Value)) : _esperas.JanelaRedundante) + _esperas.Folga;
                Avisar(aviso, $"chamada repetida cedo demais, esperando {espera.TotalSeconds:0} s");
                await _esperar(espera, ct);
                continue;
            }
            // Omie also answers bad parameters with 500 ("preenchimento inválido"). Retrying those only counts
            // toward the 30-minute block (10 errors on the same IP + key + method), so 500 is retried only when
            // the text says it is transient
            var codigo = (int)http.StatusCode;
            // a non-JSON body is usually a proxy page and worth retrying, but Omie's SOAP "Bad Request" envelope
            // means the request itself is wrong and every retry counts toward the block
            var soapRecusado = dados is null && falha.Contains("Bad Request") && falha.Contains("Fault");
            var transitorio = !soapRecusado && (codigo is 429 or 502 or 503 || dados is null || falha.Contains("Too many")
                              || Transitorio().IsMatch(falha));
            if (transitorio && tentativa < Tentativas - 1)
            {
                var espera = _esperas.Transitorio(tentativa);
                Avisar(aviso, $"o Omie respondeu {codigo} ({Recortar(falha, 90)}), tentando de novo em {espera.TotalSeconds:0} s");
                await _esperar(espera, ct);
                continue;
            }
            throw new OmieException(codigo, falha);
        }
        throw new OmieException(-1, $"desisti depois de {Tentativas} tentativas: {ultimo}");
    }

    private void Avisar(Action<string>? aviso, string texto) => aviso?.Invoke($"{Empresa.Nome}: {texto}");

    /// <summary>Names come back with HTML entities ("B&amp;apos;LOG"); every string in the response is unescaped.</summary>
    public static JsonNode? DesfazerEntidades(JsonNode? no)
    {
        switch (no)
        {
            case JsonObject obj:
                foreach (var chave in obj.Select(p => p.Key).ToList())
                    obj[chave] = DesfazerEntidades(obj[chave]?.DeepClone());
                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                    arr[i] = DesfazerEntidades(arr[i]?.DeepClone());
                return arr;
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Contains('&'):
                return JsonValue.Create(WebUtility.HtmlDecode(s));
            default:
                return no;
        }
    }

    /// <summary>Rows in a response page: the length of its biggest array.</summary>
    public static int ContarLinhas(JsonObject resposta) =>
        resposta.Select(p => p.Value).OfType<JsonArray>().Select(a => a.Count).DefaultIfEmpty(1).Max();

    public static string Canonico(JsonNode? no) => no switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal)
                                                 .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonico(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonico)) + "]",
        null => "null",
        _ => no.ToJsonString(),
    };

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto[..max];

    [GeneratedRegex(@"(\d+) segundos")]
    private static partial Regex Segundos();

    [GeneratedRegex("novamente|instantes|sendo executada|timeout", RegexOptions.IgnoreCase)]
    private static partial Regex Transitorio();
}

public sealed class OmieException(int status, string falha)
    : Exception(status > 0 ? $"O Omie respondeu HTTP {status}: {falha}" : falha)
{
    public int Status { get; } = status;
    public string Falha { get; } = falha;
}
