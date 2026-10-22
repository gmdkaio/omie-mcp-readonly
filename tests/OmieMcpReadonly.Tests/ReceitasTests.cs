using System.Net;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly.Tests;

public class ReceitasTests
{
    private static JsonObject Pagina(string lista, string estilo, params JsonObject[] itens) => estilo == "n"
        ? new JsonObject { ["nPagina"] = 1, ["nTotPaginas"] = 1, ["nRegistros"] = itens.Length, ["nTotRegistros"] = itens.Length, [lista] = new JsonArray(itens) }
        : new JsonObject { ["pagina"] = 1, ["total_de_paginas"] = 1, ["registros"] = itens.Length, ["total_de_registros"] = itens.Length, [lista] = new JsonArray(itens) };

    private static JsonObject Nf(string cnpj, string razao, string finNFe, string dCan, decimal vNF, params (string c, decimal q, decimal v, string cfop)[] itens) =>
        NfDoPedido(cnpj, razao, finNFe, dCan, vNF, "11", "N", itens);

    private static JsonObject NfDoPedido(string cnpj, string razao, string finNFe, string dCan, decimal vNF, string opPedido, string devParcial, params (string c, decimal q, decimal v, string cfop)[] itens) => new()
    {
        ["pedido"] = new JsonObject { ["opPedido"] = opPedido, ["cCancelado"] = "N", ["cDevolvido"] = "N", ["cDevParcial"] = devParcial },
        ["ide"] = new JsonObject { ["dEmi"] = "10/03/2026", ["tpNF"] = "1", ["finNFe"] = finNFe, ["dCan"] = dCan, ["dInut"] = "", ["cDeneg"] = "N" },
        ["nfDestInt"] = new JsonObject { ["cnpj_cpf"] = cnpj, ["cRazao"] = razao },
        ["det"] = new JsonArray(itens.Select(i => (JsonNode)new JsonObject { ["prod"] = new JsonObject { ["cProd"] = i.c, ["xProd"] = "Produto " + i.c, ["qCom"] = i.q, ["vProd"] = i.v, ["uCom"] = "MI", ["CFOP"] = i.cfop } }).ToArray()),
        ["total"] = new JsonObject { ["ICMSTot"] = new JsonObject { ["vNF"] = vNF } },
    };

    private static JsonObject Entrada(string cnpj, string razao, string finNFe, decimal vNF, params (string c, decimal q, decimal v, string cfop)[] itens)
    {
        var nota = NfDoPedido(cnpj, razao, finNFe, "", vNF, "", "N", itens);
        nota["ide"]!["tpNF"] = "0";
        return nota;
    }

    private static JsonObject Titulo(long cliente, string status, string venc, string prev, decimal aberto, string liquidado = "N") => new()
    {
        ["detalhes"] = new JsonObject { ["nCodCliente"] = cliente, ["cStatus"] = status, ["dDtVenc"] = venc, ["dDtPrevisao"] = prev },
        ["resumo"] = new JsonObject { ["nValAberto"] = aberto, ["cLiquidado"] = liquidado },
    };

    private static readonly string Hoje = DateTime.Now.ToString("dd/MM/yyyy");

    private static (HttpStatusCode, string) Omie(string caminho, JsonObject corpo) => corpo["call"]!.GetValue<string>() switch
    {
        // incoming invoices: one customer return (finNFe 4) and one ordinary purchase, which isn't a return
        "ListarNF" when corpo["param"]![0]!["tpNF"]!.GetValue<string>() == "0" => Ambiente.Ok(Pagina("nfCadastro", "c",
            Entrada("11", "Alfa", "4", 10m, ("P1", 1, 10m, "1.201")),
            Entrada("55", "Fornecedor", "1", 300m, ("INSUMO", 3, 300m, "1.101")))),
        "ListarNF" => Ambiente.Ok(Pagina("nfCadastro", "c",
            Nf("11", "Alfa", "1", "", 110m, ("P1", 10, 100m, "5.101")),
            // a sales order with a free sample item: Omie's report counts the whole invoice, so does the recipe
            Nf("22", "Beta", "1", "", 55.5m, ("P1", 1, 10m, "6.101"), ("P2", 40, 40m, "6.101"), ("P3", 0.2m, 0.5m, "6.911")),
            // an order returned in part: Omie's report (Situação = Autorizado) leaves the invoice out
            NfDoPedido("44", "Delta", "1", "", 1352.80m, "11", "S", ("P1", 5, 1352.80m, "5.101")),
            Nf("22", "Beta", "4", "", 999m, ("P1", 99, 999m, "5.101")),
            Nf("11", "Alfa", "1", "12/03/2026", 777m, ("P2", 77, 777m, "5.101")),
            // a mould sent to a client from a Remessa de Produto (opPedido 14): not billing
            NfDoPedido("33", "Gama", "1", "", 38000m, "14", "N", ("MOLDE", 1, 38000m, "6.949")))),
        "ListarClientes" => Ambiente.Ok(Pagina("clientes_cadastro", "c",
            new JsonObject { ["codigo_cliente_omie"] = 1, ["cnpj_cpf"] = "11", ["razao_social"] = "Alfa Ltda", ["nome_fantasia"] = "Alfa", ["info"] = new JsonObject { ["dInc"] = "03/01/2026" }, ["tags"] = new JsonArray(new JsonObject { ["tag"] = "Cliente" }, new JsonObject { ["tag"] = "Fornecedor" }) },
            new JsonObject { ["codigo_cliente_omie"] = 2, ["cnpj_cpf"] = "22", ["razao_social"] = "Beta SA", ["nome_fantasia"] = "", ["info"] = new JsonObject { ["dInc"] = "20/02/2026" }, ["tags"] = new JsonArray(new JsonObject { ["tag"] = "Fornecedor" }) },
            new JsonObject { ["codigo_cliente_omie"] = 3, ["cnpj_cpf"] = "00.000.000/0000-00", ["razao_social"] = "SIMPLES NACIONAL", ["info"] = new JsonObject { ["dInc"] = "10/01/2026" }, ["tags"] = new JsonArray() },
            new JsonObject { ["codigo_cliente_omie"] = 4, ["cnpj_cpf"] = "00.000.000/0000-00", ["razao_social"] = "FOLHA", ["info"] = new JsonObject { ["dInc"] = "10/05/2025" } })),
        "ListarMovimentos" when corpo["param"]![0]!["cTpLancamento"]!.GetValue<string>() == "CR" => Ambiente.Ok(Pagina("movimentos", "n",
            Titulo(1, "ATRASADO", "01/01/2026", "01/01/2026", 100.50m),
            Titulo(1, "ATRASADO", "01/02/2026", "01/02/2026", 0.50m),
            Titulo(2, "ATRASADO", "01/02/2026", "01/02/2026", 30m),
            Titulo(9, "ATRASADO", "01/02/2026", "01/02/2026", 5m),
            // received in part: Omie says RECEBIDO, but half is still open and overdue
            Titulo(2, "RECEBIDO", "01/03/2026", "01/03/2026", 50m),
            // settled: nothing open
            Titulo(1, "RECEBIDO", "01/03/2026", "01/03/2026", 0m, "S"),
            // due today: not overdue yet
            Titulo(9, "ATRASADO", Hoje, Hoje, 7m))),
        "ListarMovimentos" => Ambiente.Ok(Pagina("movimentos", "n",
            Titulo(2, "EMABERTO", Hoje, Hoje, 70m),
            Titulo(3, "EMABERTO", Hoje, Hoje, 5m),
            Titulo(4, "EMABERTO", Hoje, Hoje, 6m),
            Titulo(2, "EMABERTO", "01/01/2020", "01/01/2020", 1000m))),
        var m => throw new InvalidOperationException("método inesperado " + m),
    };

    [Fact]
    public async Task Todas_as_receitas_so_chamam_metodos_de_leitura()
    {
        using var amb = new Ambiente(Omie);
        await Receitas.ClientesNovos(amb.Servico, "MATRIZ", "01/01/2026", "31/03/2026");
        await Receitas.ProdutosMaisVendidos(amb.Servico, "MATRIZ", "01/03/2026", "31/03/2026");
        await Receitas.FaturamentoPorCliente(amb.Servico, "MATRIZ", "01/03/2026", "31/03/2026");
        await Receitas.ContasReceberVencidas(amb.Servico, "MATRIZ");
        await Receitas.ContasPagarProximosDias(amb.Servico, "MATRIZ");

        Assert.NotEmpty(amb.Omie.Pedidos);
        Assert.All(amb.Omie.Pedidos, p =>
            Assert.Null(ReadOnlyGuard.Recusa(new Uri(p.Url).AbsolutePath["/api/v1/".Length..], p.Corpo["call"]!.GetValue<string>())));
    }

    [Fact]
    public async Task Produtos_mais_vendidos_tira_devolucao_e_cancelada()
    {
        using var amb = new Ambiente(Omie);
        var r = JsonNode.Parse(await Receitas.ProdutosMaisVendidos(amb.Servico, "MATRIZ", "01/03/2026", "31/03/2026"))!;
        var grupos = r["resumo"]!["grupos"]!.AsArray();

        Assert.Equal("P1", grupos[0]!["grupo"]!.GetValue<string>());
        Assert.Equal(110m, grupos[0]!["somas"]!["det.prod.vProd"]!.GetValue<decimal>());
        Assert.Equal(11m, grupos[0]!["somas"]!["det.prod.qCom"]!.GetValue<decimal>());
        Assert.Equal(10m, grupos[0]!["preco_medio"]!.GetValue<decimal>());
        Assert.Equal("MI", grupos[0]!["unidade"]!.GetValue<string>());
        Assert.Equal(40m, grupos[1]!["somas"]!["det.prod.vProd"]!.GetValue<decimal>());
        // items of sales-order invoices only: the sample (P3) is in, the mould remessa and the returned order are out
        Assert.Equal(3, r["resumo"]!["total_de_grupos"]!.GetValue<int>());
        Assert.Equal("S", amb.Omie.Pedidos.First(p => p.Corpo["call"]!.GetValue<string>() == "ListarNF").Corpo["param"]![0]!["cDetalhesPedido"]!.GetValue<string>());
        Assert.Equal("P1", r["devolucoes"]!["grupos"]![0]!["grupo"]!.GetValue<string>());
        Assert.Equal(r["resumo"]!["somas"]!["det.prod.vProd"]!.GetValue<decimal>() - 10m, r["receita_menos_devolucoes"]!.GetValue<decimal>());
        Assert.True(r["conferida_com_tela_do_omie"]!.GetValue<bool>());

        var porQuantidade = JsonNode.Parse(await Receitas.ProdutosMaisVendidos(amb.Servico, "MATRIZ", "01/03/2026", "31/03/2026", "quantidade"))!;
        Assert.Equal("P2", porQuantidade["resumo"]!["grupos"]![0]!["grupo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Faturamento_por_cliente_agrupa_por_cnpj()
    {
        using var amb = new Ambiente(Omie);
        var r = JsonNode.Parse(await Receitas.FaturamentoPorCliente(amb.Servico, "MATRIZ", "01/03/2026", "31/03/2026"))!;
        var grupos = r["resumo"]!["grupos"]!.AsArray();
        // the mould remessa (Gama) and the partly returned order (Delta) are left out
        Assert.Equal(["11", "22"], grupos.Select(g => g!["grupo"]!.GetValue<string>()));
        Assert.Equal("Alfa", grupos[0]!["rotulo"]!.GetValue<string>());
        Assert.Equal(165.5m, r["resumo"]!["somas"]!["total.ICMSTot.vNF"]!.GetValue<decimal>());
        // checked against Omie's billing report, so no "not checked yet" warning for the user
        Assert.True(r["conferida_com_tela_do_omie"]!.GetValue<bool>());
        Assert.Null(r["aviso_conferencia"]);
        // the customer return comes apart and the net figure is computed here; the purchase isn't a return
        Assert.Equal(10m, r["devolucoes"]!["somas"]!["total.ICMSTot.vNF"]!.GetValue<decimal>());
        Assert.Equal(1, r["devolucoes"]!["linhas_consideradas"]!.GetValue<int>());
        Assert.Equal(155.5m, r["faturamento_menos_devolucoes"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Vencidas_por_cliente_com_nome_do_cadastro()
    {
        using var amb = new Ambiente(Omie);
        var r = JsonNode.Parse(await Receitas.ContasReceberVencidas(amb.Servico, "MATRIZ"))!;
        var grupos = r["resumo"]!["grupos"]!.AsArray();

        Assert.Equal(186m, r["resumo"]!["somas"]!["resumo.nValAberto"]!.GetValue<decimal>());
        Assert.Equal(5, r["resumo"]!["linhas_consideradas"]!.GetValue<int>());
        Assert.Equal("Alfa", grupos[0]!["rotulo"]!.GetValue<string>());
        Assert.Equal(101m, grupos[0]!["somas"]!["resumo.nValAberto"]!.GetValue<decimal>());
        Assert.Equal("Beta SA", grupos[1]!["rotulo"]!.GetValue<string>());
        Assert.Equal(80m, grupos[1]!["somas"]!["resumo.nValAberto"]!.GetValue<decimal>());
        Assert.True(r["conferida_com_tela_do_omie"]!.GetValue<bool>());
        Assert.Equal("(sem cadastro)", grupos[2]!["rotulo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Contas_a_pagar_so_dentro_da_janela()
    {
        using var amb = new Ambiente(Omie);
        var r = JsonNode.Parse(await Receitas.ContasPagarProximosDias(amb.Servico, "MATRIZ", dias: 30))!;
        Assert.Equal(81m, r["resumo"]!["somas"]!["resumo.nValAberto"]!.GetValue<decimal>());
        // two payees with the all-zeros CNPJ stay apart
        Assert.Equal(3, r["resumo"]!["total_de_grupos"]!.GetValue<int>());
        var param = r["consulta"]!["parametros"]!;
        Assert.Equal(Hoje, param["dDtPrevDe"]!.GetValue<string>());
        Assert.Equal(DateTime.Now.AddDays(29).ToString("dd/MM/yyyy"), param["dDtPrevAte"]!.GetValue<string>());
    }

    [Fact]
    public async Task Clientes_novos_por_mes_e_por_tag()
    {
        using var amb = new Ambiente(Omie);
        var r = JsonNode.Parse(await Receitas.ClientesNovos(amb.Servico, "MATRIZ", "01/01/2026", "31/01/2026"))!;
        // January: one cadastro tagged Cliente, one with no tag; only the first is a new client
        Assert.Equal(1, r["clientes_novos"]!.GetValue<int>());
        Assert.Equal("2026-01", r["por_mes"]!["grupos"]![0]!["grupo"]!.GetValue<string>());
        var todos = r["todos_os_cadastros"]!;
        Assert.Equal(2, todos["cadastros_incluidos"]!.GetValue<int>());
        Assert.Equal("Cliente", todos["por_tag"]!["grupos"]![0]!["grupo"]!.GetValue<string>());
        Assert.Equal(1, todos["sem_tag"]!.GetValue<int>());
        Assert.Equal("S", amb.Omie.Pedidos[0].Corpo["param"]![0]!["filtrar_apenas_inclusao"]!.GetValue<string>());
    }
}
