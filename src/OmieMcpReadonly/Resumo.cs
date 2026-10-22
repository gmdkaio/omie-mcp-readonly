using System.Globalization;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly;

public sealed record Filtro(string Campo, string Operador, JsonNode? Valor = null);

/// <param name="Expandir">Array to unnest before anything else ("det" on invoices: one row per item, parent fields kept).</param>
/// <param name="Somar">Fields to add up.</param>
/// <param name="AgruparPor">One field to group by.</param>
/// <param name="Periodo">"dia", "mes" or "ano" when <paramref name="AgruparPor"/> is a date.</param>
/// <param name="Rotulo">Field shown next to each group (a name for a code), taken from the group's first row.</param>
/// <param name="OrdenarPor">"contagem", "grupo" or one of the <paramref name="Somar"/> fields; default the first sum, else the count.</param>
/// <param name="Top">Groups to return; the rest are added up in "outros".</param>
public sealed record PedidoResumo(
    string? Expandir = null,
    IReadOnlyList<Filtro>? Filtros = null,
    IReadOnlyList<string>? Somar = null,
    string? AgruparPor = null,
    string? Periodo = null,
    string? Rotulo = null,
    string? OrdenarPor = null,
    int? Top = null);

/// <summary>
/// Counts, sums and groupings computed here over every row, so an answer never depends on Claude adding up a table.
/// Sums use decimal, so R$ totals come out to the cent.
/// </summary>
public static class Resumo
{
    public const int TopPadrao = 50;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Operadores = ["=", "!=", ">", ">=", "<", "<=", "contem", "nao_contem", "em", "nao_em", "vazio", "preenchido"];

    public static JsonObject Resumir(IReadOnlyList<JsonObject> linhas, PedidoResumo pedido)
    {
        var somar = pedido.Somar ?? [];
        var filtros = pedido.Filtros ?? [];
        foreach (var f in filtros)
            if (!Operadores.Contains(f.Operador))
                throw new ArgumentException($"Operador \"{f.Operador}\" desconhecido. Use um destes: {string.Join(" ", Operadores)}.");
        if (pedido.Periodo is not null and not ("dia" or "mes" or "ano"))
            throw new ArgumentException("periodo deve ser dia, mes ou ano.");

        var consideradas = (pedido.Expandir is null ? linhas : linhas.SelectMany(l => Expandir(l, pedido.Expandir)))
            .Where(l => filtros.All(f => Passa(l, f)))
            .ToList();

        var resultado = new JsonObject
        {
            ["linhas_lidas"] = linhas.Count,
            ["linhas_consideradas"] = consideradas.Count,
        };
        if (pedido.Expandir is not null)
            resultado["observacao_expandir"] = $"cada linha é um item de \"{pedido.Expandir}\"";

        var (somas, semValor) = Somar(consideradas, somar);
        if (somar.Count > 0)
        {
            resultado["somas"] = somas;
            if (semValor.Count > 0) resultado["linhas_sem_valor_numerico"] = semValor;
        }

        if (pedido.AgruparPor is null)
            return resultado;

        var grupos = consideradas
            .GroupBy(l => ChaveGrupo(l, pedido.AgruparPor, pedido.Periodo))
            .Select(g => (Chave: g.Key, Linhas: g.ToList(), Somas: Somar(g.ToList(), somar).Somas))
            .ToList();

        var ordem = pedido.OrdenarPor ?? (somar.Count > 0 ? somar[0] : "contagem");
        IEnumerable<(string Chave, List<JsonObject> Linhas, JsonObject Somas)> ordenados = ordem switch
        {
            "grupo" => grupos.OrderBy(g => g.Chave, StringComparer.Ordinal),
            "contagem" => grupos.OrderByDescending(g => g.Linhas.Count).ThenBy(g => g.Chave, StringComparer.Ordinal),
            _ when somar.Contains(ordem) => grupos.OrderByDescending(g => g.Somas[ordem]!.GetValue<decimal>()).ThenBy(g => g.Chave, StringComparer.Ordinal),
            _ => throw new ArgumentException($"ordenar_por deve ser contagem, grupo ou um dos campos somados ({string.Join(", ", somar)})."),
        };
        var lista = ordenados.ToList();
        var top = Math.Max(1, pedido.Top ?? TopPadrao);

        var saida = new JsonArray();
        foreach (var g in lista.Take(top))
        {
            var item = new JsonObject { ["grupo"] = g.Chave };
            if (pedido.Rotulo is not null)
                item["rotulo"] = g.Linhas.Select(l => Texto(Valor(l, pedido.Rotulo))).FirstOrDefault(t => t is not null);
            item["contagem"] = g.Linhas.Count;
            if (somar.Count > 0) item["somas"] = g.Somas;
            saida.Add(item);
        }
        resultado["agrupado_por"] = pedido.AgruparPor + (pedido.Periodo is null ? "" : $" (por {pedido.Periodo})");
        resultado["ordenado_por"] = ordem;
        resultado["total_de_grupos"] = lista.Count;
        resultado["grupos"] = saida;
        if (lista.Count > top)
        {
            var resto = lista.Skip(top).SelectMany(g => g.Linhas).ToList();
            var outros = new JsonObject { ["grupos"] = lista.Count - top, ["contagem"] = resto.Count };
            if (somar.Count > 0) outros["somas"] = Somar(resto, somar).Somas;
            resultado["outros"] = outros;
        }
        return resultado;
    }

    /// <summary>The value at a dotted path ("ide.dEmi", "det.prod.vProd"). Null when missing or when it crosses an array.</summary>
    public static JsonNode? Valor(JsonObject linha, string caminho)
    {
        JsonNode? no = linha;
        foreach (var parte in caminho.Split('.'))
            no = no is JsonObject o ? o[parte] : null;
        return no;
    }

    public static decimal? Numero(JsonNode? no)
    {
        if (no is not JsonValue v) return null;
        decimal d;
        // parsed from the JSON text: TryGetValue<decimal> fails on a value built from an int or a double, and going
        // through double would lose cents
        if (v.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            return decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : null;
        if (!v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        // "1234.56" from the API, "1.234,56" if a text field carries a Brazilian number
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d) && !s.Contains(','))
            return d;
        return decimal.TryParse(s, NumberStyles.Number, PtBr, out d) ? d : null;
    }

    public static DateOnly? Data(JsonNode? no) =>
        no is JsonValue v && v.TryGetValue<string>(out var s)
        && DateOnly.TryParseExact(s.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static string? Texto(JsonNode? no) => no switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        JsonValue v => v.ToJsonString(),
        JsonArray { Count: 0 } => null,
        _ => no.ToJsonString(),
    };

    private static IEnumerable<JsonObject> Expandir(JsonObject linha, string caminho)
    {
        if (Valor(linha, caminho) is not JsonArray itens)
            yield break;
        var partes = caminho.Split('.');
        foreach (var item in itens)
        {
            var copia = (JsonObject)linha.DeepClone();
            var pai = partes[..^1].Aggregate((JsonNode?)copia, (no, p) => no?[p]) as JsonObject;
            pai![partes[^1]] = item?.DeepClone();
            yield return copia;
        }
    }

    private static (JsonObject Somas, JsonObject SemValor) Somar(List<JsonObject> linhas, IReadOnlyList<string> campos)
    {
        var somas = new JsonObject();
        var semValor = new JsonObject();
        foreach (var campo in campos)
        {
            decimal total = 0;
            var faltando = 0;
            foreach (var l in linhas)
            {
                if (Numero(Valor(l, campo)) is { } n) total += n;
                else faltando++;
            }
            somas[campo] = total;
            if (faltando > 0) semValor[campo] = faltando;
        }
        return (somas, semValor);
    }

    private static string ChaveGrupo(JsonObject linha, string campo, string? periodo)
    {
        var no = Valor(linha, campo);
        if (periodo is not null && Data(no) is { } d)
            return periodo switch { "dia" => d.ToString("yyyy-MM-dd"), "mes" => d.ToString("yyyy-MM"), _ => d.ToString("yyyy") };
        return Texto(no) ?? "(vazio)";
    }

    private static bool Passa(JsonObject linha, Filtro f)
    {
        var no = Valor(linha, f.Campo);
        var texto = Texto(no);
        switch (f.Operador)
        {
            case "vazio": return texto is null;
            case "preenchido": return texto is not null;
            case "em": return Lista(f.Valor).Any(v => Igual(no, v));
            case "nao_em": return !Lista(f.Valor).Any(v => Igual(no, v));
            case "=": return Igual(no, f.Valor);
            case "!=": return !Igual(no, f.Valor);
            case "contem": return texto is not null && texto.Contains(Texto(f.Valor) ?? "", StringComparison.OrdinalIgnoreCase);
            case "nao_contem": return texto is null || !texto.Contains(Texto(f.Valor) ?? "", StringComparison.OrdinalIgnoreCase);
        }
        var cmp = Comparar(no, f.Valor);
        if (cmp is null) return false;
        return f.Operador switch { ">" => cmp > 0, ">=" => cmp >= 0, "<" => cmp < 0, _ => cmp <= 0 };
    }

    private static IEnumerable<JsonNode?> Lista(JsonNode? valor) => valor is JsonArray a ? a : [valor];

    private static bool Igual(JsonNode? a, JsonNode? b)
    {
        if (Data(a) is { } da && Data(b) is { } db) return da == db;
        if (Numero(a) is { } na && Numero(b) is { } nb) return na == nb;
        return string.Equals(Texto(a), Texto(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Dates as dates (dd/mm/aaaa doesn't sort as text), numbers as numbers, the rest as text.</summary>
    private static int? Comparar(JsonNode? a, JsonNode? b)
    {
        if (Data(a) is { } da && Data(b) is { } db) return da.CompareTo(db);
        if (Numero(a) is { } na && Numero(b) is { } nb) return na.CompareTo(nb);
        var (ta, tb) = (Texto(a), Texto(b));
        return ta is null || tb is null ? null : string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase);
    }
}
