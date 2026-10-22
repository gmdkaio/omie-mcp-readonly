using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly;

/// <summary>
/// One JSON line per Omie call (and per refusal) in %APPDATA%\omie-mcp-readonly\chamadas-AAAA-MM-DD.jsonl, so
/// anyone can check afterwards what the connector asked Omie. Keeps time, company, method, filters, rows and
/// duration; never the keys and never the data that came back. Files older than 30 days are deleted at startup.
/// </summary>
public sealed class CallLog
{
    public const int DiasGuardados = 30;
    private readonly object _trava = new();

    public string Pasta { get; }

    public CallLog(string? pasta = null)
    {
        Pasta = pasta
                ?? Environment.GetEnvironmentVariable("OMIE_MCP_LOG_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "omie-mcp-readonly");
        Directory.CreateDirectory(Pasta);
    }

    public void ApagarAntigos(DateTime agora)
    {
        foreach (var arquivo in Directory.EnumerateFiles(Pasta, "chamadas-*.jsonl"))
        {
            var nome = Path.GetFileNameWithoutExtension(arquivo)["chamadas-".Length..];
            if (DateTime.TryParseExact(nome, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var dia)
                && dia < agora.Date.AddDays(-DiasGuardados))
                File.Delete(arquivo);
        }
    }

    public void Registrar(string empresa, string endpoint, string metodo, JsonObject? filtros, string resultado,
                          int? linhas, TimeSpan duracao)
    {
        var agora = DateTime.Now;
        var linha = new JsonObject
        {
            ["hora"] = agora.ToString("yyyy-MM-ddTHH:mm:ss"),
            ["empresa"] = empresa,
            ["endpoint"] = endpoint,
            ["metodo"] = metodo,
            ["filtros"] = filtros?.DeepClone(),
            ["resultado"] = resultado,
            ["linhas"] = linhas,
            ["duracao_ms"] = (long)duracao.TotalMilliseconds,
        };
        var texto = linha.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        lock (_trava)
            File.AppendAllText(Path.Combine(Pasta, $"chamadas-{agora:yyyy-MM-dd}.jsonl"), texto + Environment.NewLine);
    }
}
