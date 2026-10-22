using System.Text.Json.Nodes;

namespace OmieMcpReadonly.Tests;

public class ResumoTests
{
    private static JsonObject Nota(string nNF, string dEmi, string finNFe, string? dCan, params (string cProd, string xProd, decimal q, decimal v)[] itens) => new()
    {
        ["ide"] = new JsonObject { ["nNF"] = nNF, ["dEmi"] = dEmi, ["tpNF"] = "1", ["finNFe"] = finNFe, ["dCan"] = dCan ?? "" },
        ["det"] = new JsonArray(itens.Select(i => (JsonNode)new JsonObject
        {
            ["prod"] = new JsonObject { ["cProd"] = i.cProd, ["xProd"] = i.xProd, ["qCom"] = i.q, ["vProd"] = i.v },
        }).ToArray()),
        ["total"] = new JsonObject { ["ICMSTot"] = new JsonObject { ["vNF"] = itens.Sum(i => i.v) } },
    };

    private static readonly List<JsonObject> Notas =
    [
        Nota("1", "05/01/2026", "1", null, ("A", "Tubo", 10, 100.10m), ("B", "Joelho", 5, 20.05m)),
        Nota("2", "20/01/2026", "1", null, ("A", "Tubo", 3, 30.03m)),
        Nota("3", "02/02/2026", "4", null, ("A", "Tubo", 1, 10.01m)),      // devolução
        Nota("4", "15/02/2026", "1", "16/02/2026", ("B", "Joelho", 9, 99m)), // cancelada
        Nota("5", "28/02/2026", "1", null, ("C", "Luva", 2, 0.02m)),
    ];

    private static readonly Filtro[] Validas = [new("ide.finNFe", "!=", "4"), new("ide.dCan", "vazio")];

    [Fact]
    public void Expande_itens_filtra_e_agrupa_por_produto()
    {
        var r = Resumo.Resumir(Notas, new PedidoResumo(Expandir: "det", Filtros: Validas,
            Somar: ["det.prod.vProd", "det.prod.qCom"], AgruparPor: "det.prod.cProd", Rotulo: "det.prod.xProd"));

        Assert.Equal(4, r["linhas_consideradas"]!.GetValue<int>());
        Assert.Equal(150.20m, r["somas"]!["det.prod.vProd"]!.GetValue<decimal>());
        var grupos = r["grupos"]!.AsArray();
        Assert.Equal("A", grupos[0]!["grupo"]!.GetValue<string>());
        Assert.Equal("Tubo", grupos[0]!["rotulo"]!.GetValue<string>());
        Assert.Equal(130.13m, grupos[0]!["somas"]!["det.prod.vProd"]!.GetValue<decimal>());
        Assert.Equal(13m, grupos[0]!["somas"]!["det.prod.qCom"]!.GetValue<decimal>());
        // the parent invoice fields stay visible after expanding
        Assert.Equal(["A", "B", "C"], grupos.Select(g => g!["grupo"]!.GetValue<string>()));
    }

    [Fact]
    public void Top_soma_o_resto_em_outros()
    {
        var r = Resumo.Resumir(Notas, new PedidoResumo(Expandir: "det", Filtros: Validas,
            Somar: ["det.prod.vProd"], AgruparPor: "det.prod.cProd", Top: 1));
        Assert.Single(r["grupos"]!.AsArray());
        Assert.Equal(2, r["outros"]!["grupos"]!.GetValue<int>());
        Assert.Equal(20.07m, r["outros"]!["somas"]!["det.prod.vProd"]!.GetValue<decimal>());
    }

    [Fact]
    public void Datas_comparam_como_datas_e_agrupam_por_mes()
    {
        // as text "05/01/2026" > "02/02/2026"; as dates it is the other way round
        var r = Resumo.Resumir(Notas, new PedidoResumo(
            Filtros: [new("ide.dEmi", ">=", "01/02/2026"), new("ide.dEmi", "<=", "28/02/2026")],
            AgruparPor: "ide.dEmi", Periodo: "mes"));
        Assert.Equal(3, r["linhas_consideradas"]!.GetValue<int>());
        Assert.Equal("2026-02", r["grupos"]![0]!["grupo"]!.GetValue<string>());
    }

    [Fact]
    public void Operador_em_aceita_lista_e_numero_igual_a_texto()
    {
        var r = Resumo.Resumir(Notas, new PedidoResumo(Filtros: [new("ide.nNF", "em", new JsonArray(1, 5))]));
        Assert.Equal(2, r["linhas_consideradas"]!.GetValue<int>());
    }

    [Fact]
    public void Conta_linhas_sem_valor_numerico()
    {
        var linhas = new List<JsonObject> { new() { ["v"] = 1 }, new() { ["v"] = "abc" }, new() { ["x"] = 1 } };
        var r = Resumo.Resumir(linhas, new PedidoResumo(Somar: ["v"]));
        Assert.Equal(1m, r["somas"]!["v"]!.GetValue<decimal>());
        Assert.Equal(2, r["linhas_sem_valor_numerico"]!["v"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("1234.56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("10", "10")]
    public void Le_numeros_em_texto(string texto, string esperado) =>
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), Resumo.Numero(JsonValue.Create(texto)));

    [Fact]
    public void Operador_desconhecido_e_erro_claro()
    {
        var e = Assert.Throws<ArgumentException>(() => Resumo.Resumir(Notas, new PedidoResumo(Filtros: [new("x", "like", "a")])));
        Assert.Contains("contem", e.Message);
    }

    [Fact]
    public void Crescente_poe_as_maiores_saidas_primeiro_e_medias_saem_prontas()
    {
        var linhas = new List<JsonObject>
        {
            new() { ["cat"] = "Aluguel", ["v"] = -40m }, new() { ["cat"] = "Resina", ["v"] = -100m },
            new() { ["cat"] = "Resina", ["v"] = -60m }, new() { ["cat"] = "Venda", ["v"] = 250m },
        };
        var r = Resumo.Resumir(linhas, new PedidoResumo(Somar: ["v"], AgruparPor: "cat", Crescente: true));
        Assert.Equal(["Resina", "Aluguel", "Venda"], r["grupos"]!.AsArray().Select(g => g!["grupo"]!.GetValue<string>()));
        Assert.Equal(12.5m, r["medias"]!["v"]!.GetValue<decimal>());
        Assert.Equal(-80m, r["grupos"]![0]!["medias"]!["v"]!.GetValue<decimal>());
    }
}