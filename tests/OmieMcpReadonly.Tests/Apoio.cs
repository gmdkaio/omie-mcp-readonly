using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly.Tests;

/// <summary>Stands in for Omie: answers each request with whatever the test decides and keeps what was sent.</summary>
public sealed class OmieFalso(Func<string, JsonObject, (HttpStatusCode, string)> responder) : HttpMessageHandler
{
    public List<(string Url, JsonObject Corpo)> Pedidos { get; } = [];
    public List<string> TiposDeConteudo { get; } = [];
    public TimeSpan Atraso { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var corpo = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
        lock (Pedidos)
        {
            Pedidos.Add((request.RequestUri!.ToString(), corpo));
            TiposDeConteudo.Add(request.Content.Headers.ContentType!.ToString());
        }
        if (Atraso > TimeSpan.Zero) await Task.Delay(Atraso, ct);
        var (status, texto) = responder(request.RequestUri!.AbsolutePath, corpo);
        return new HttpResponseMessage(status) { Content = new StringContent(texto, Encoding.UTF8, "application/json") };
    }
}

public sealed class Ambiente : IDisposable
{
    public OmieFalso Omie { get; }
    public string PastaLog { get; } = Path.Combine(Path.GetTempPath(), "omie-mcp-testes-" + Guid.NewGuid().ToString("N"));
    public CallLog Log { get; }
    public List<TimeSpan> Esperas { get; } = [];
    public Empresas Empresas { get; }
    public Servico Servico { get; }
    public Dictionary<string, OmieClient> Clientes { get; }

    public Ambiente(Func<string, JsonObject, (HttpStatusCode, string)> responder, TimeSpan? esperaMaxima = null, params string[] nomes)
    {
        Omie = new OmieFalso(responder);
        Log = new CallLog(PastaLog);
        Empresas = new Empresas((nomes.Length == 0 ? ["MATRIZ"] : nomes).Select(n => new Empresa(n, $"key-{n}", $"secret-{n}")));
        var http = new HttpClient(Omie);
        Clientes = Empresas.Lista.ToDictionary(e => e.Nome, e => new OmieClient(e, http, Log, esperar: (t, _) =>
        {
            lock (Esperas) Esperas.Add(t);
            return Task.CompletedTask;
        }));
        Servico = new Servico(Empresas, new Buscador(Clientes), esperaMaxima ?? TimeSpan.FromSeconds(30));
    }

    public OmieClient Cliente => Clientes.Values.First();

    public string[] LinhasDoLog() =>
        Directory.EnumerateFiles(PastaLog, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();

    public void Dispose()
    {
        try { Directory.Delete(PastaLog, true); } catch (IOException) { }
    }

    public static (HttpStatusCode, string) Ok(JsonObject o) => (HttpStatusCode.OK, o.ToJsonString());
    public static (HttpStatusCode, string) Falha(HttpStatusCode status, string texto) =>
        (status, new JsonObject { ["faultstring"] = texto, ["faultcode"] = "SOAP-ENV:Client-5113" }.ToJsonString());
}
