using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace OmieMcpReadonly;

/// <summary>
/// Fixed recipes for the questions asked most. Each one is a resumir with the filters and definitions decided once,
/// so the same question always gets the same method. A recipe ships with conferida: true only after its numbers
/// matched the matching Omie screen for a chosen period (docs/receitas.md has the record of each check).
/// </summary>
[McpServerToolType]
public sealed class Receitas
{
    private const string Pendente = "Esta receita ainda não foi conferida contra a tela do Omie. Diga isso ao usuário junto com a resposta.";

    // invoices that count as billing: issued (tpNF 1), not cancelled, not voided, not denied, not a return
    private static readonly Filtro[] NotasValidas =
    [
        new("ide.tpNF", "=", "1"),
        new("ide.finNFe", "!=", "4"),
        new("ide.dCan", "vazio"),
        new("ide.dInut", "vazio"),
        new("ide.cDeneg", "!=", "S"),
    ];

    private const string DefinicaoNotas =
        "NF-e de saída (tpNF 1) emitidas no período (ide.dEmi), sem as canceladas (ide.dCan), inutilizadas, denegadas " +
        "e devoluções (finNFe 4). Todas as naturezas de operação entram (CFOP não é filtrado).";

    [McpServerTool(Name = "clientes_novos", Title = "Clientes novos por período", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("How many new clients in a period (cadastros included in Omie with the tag Cliente), month by month, plus every cadastro included in the period broken down by tag (Fornecedor, Transportadora, no tag...). Use for \"quantos clientes novos\"; the headline number is clientes_novos.")]
    public static Task<string> ClientesNovos(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("Start date dd/mm/aaaa.")] string de,
        [Description("End date dd/mm/aaaa.")] string ate,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            (de, ate) = (Servico.Data(de, "de"), Servico.Data(ate, "ate"));
            var param = new JsonObject
            {
                ["filtrar_por_data_de"] = de,
                ["filtrar_por_data_ate"] = ate,
                ["filtrar_apenas_inclusao"] = "S",
                ["apenas_importado_api"] = "N",
            };
            var buscas = servico.Iniciar(servico.Empresas.Resolver(empresa), "geral/clientes", "ListarClientes", param);
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento(buscas);

            // the date filter is applied again here: Omie's filter is the first line, this one is the definition
            Filtro[] noPeriodo = [new("info.dInc", ">=", de), new("info.dInc", "<=", ate)];
            // the client register also holds suppliers, carriers, staff and cadastros with no tag: a new client is
            // one with the tag Cliente. Unnesting tags gives one row per tag, and a cadastro has Cliente at most once
            Filtro[] clientes = [.. noPeriodo, new("tags.tag", "=", "Cliente")];
            var linhas = Servico.Linhas(buscas);
            var saida = Cabecalho("clientes_novos", buscas, "geral/clientes", "ListarClientes", param, clientes, conferida: false,
                $"Cliente novo = cadastro incluído no Omie (data de inclusão, info.dInc) entre {de} e {ate} e marcado " +
                "com a tag Cliente. O cadastro do Omie também guarda fornecedores, transportadoras, prestadores e " +
                "cadastros sem tag: esses não contam como cliente novo e aparecem só no detalhamento por tag. Cadastros " +
                "inativos contam. Um cadastro com as tags Cliente e Fornecedor conta como cliente.");
            var porMes = Resumo.Resumir(linhas, new PedidoResumo(Expandir: "tags", Filtros: clientes, AgruparPor: "info.dInc", Periodo: "mes", OrdenarPor: "grupo", Top: 1000));
            saida["clientes_novos"] = porMes["linhas_consideradas"]!.DeepClone();
            saida["por_mes"] = porMes;
            if (buscas.Count > 1)
                saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(Expandir: "tags", Filtros: clientes, AgruparPor: "_empresa", OrdenarPor: "grupo"));
            var detalhe = new JsonObject
            {
                ["cadastros_incluidos"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: noPeriodo))["linhas_consideradas"]!.DeepClone(),
                ["por_tag"] = Resumo.Resumir(linhas, new PedidoResumo(Expandir: "tags", Filtros: noPeriodo, AgruparPor: "tags.tag", OrdenarPor: "contagem")),
                // a cadastro can have several tags or none; the ones with none don't show up in por_tag at all
                ["sem_tag"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: [.. noPeriodo, new("tags", "vazio")]))["linhas_consideradas"]!.DeepClone(),
            };
            saida["todos_os_cadastros"] = detalhe;
            return Finalizar(saida, buscas);
        });

    [McpServerTool(Name = "produtos_mais_vendidos", Title = "Produtos mais vendidos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Ranking of products sold in a period from issued invoices (NF-e), with quantity and revenue (receita) per product. Use for \"produtos mais vendidos\", \"quantidade x preço\". Based on billing, not on orders (pedidos).")]
    public static Task<string> ProdutosMaisVendidos(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("Start date dd/mm/aaaa (emission).")] string de,
        [Description("End date dd/mm/aaaa (emission).")] string ate,
        [Description("receita (default) or quantidade.")] string ordenar_por = "receita",
        [Description("How many products (default 20).")] int top = 20,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            var ordem = ordenar_por?.Trim().ToLowerInvariant() switch
            {
                null or "" or "receita" => "det.prod.vProd",
                "quantidade" => "det.prod.qCom",
                _ => throw new ArgumentException("ordenar_por deve ser receita ou quantidade."),
            };
            var (buscas, param) = BuscarNotas(servico, empresa, ref de, ref ate);
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento(buscas);

            var linhas = Servico.Linhas(buscas);
            var saida = Cabecalho("produtos_mais_vendidos", buscas, "produtos/nfconsultar", "ListarNF", param, NotasValidas, conferida: false,
                DefinicaoNotas + " Uma linha por item de nota (det). Receita = soma de det.prod.vProd (valor bruto dos " +
                "produtos, sem IPI, ST e frete, antes de descontos); quantidade = soma de det.prod.qCom na unidade " +
                "comercial de cada produto (campo unidade; MI = milheiro, então 2,6 MI são 2.600 peças); preço médio = " +
                "receita / quantidade, por essa unidade. Ao ordenar por quantidade, produtos em unidades diferentes " +
                "não são comparáveis entre si: diga a unidade junto de cada quantidade. A soma geral de quantidade " +
                "mistura unidades e não deve ser citada.");
            var resumo = Resumo.Resumir(linhas, new PedidoResumo(
                Expandir: "det", Filtros: NotasValidas, Somar: ["det.prod.vProd", "det.prod.qCom"],
                AgruparPor: "det.prod.cProd", Rotulo: "det.prod.xProd", OrdenarPor: ordem, Top: Math.Clamp(top, 1, 500)));
            AdicionarPrecoMedioEUnidade(resumo, linhas);
            saida["resumo"] = resumo;
            return Finalizar(saida, buscas);
        });

    [McpServerTool(Name = "faturamento_por_cliente", Title = "Faturamento por cliente", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Billing (faturamento) per client in a period, from issued invoices, ranked by total invoiced. Use for \"quanto faturamos com cada cliente\", \"maiores clientes\".")]
    public static Task<string> FaturamentoPorCliente(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("Start date dd/mm/aaaa (emission).")] string de,
        [Description("End date dd/mm/aaaa (emission).")] string ate,
        [Description("How many clients (default 30).")] int top = 30,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            var (buscas, param) = BuscarNotas(servico, empresa, ref de, ref ate);
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento(buscas);

            var linhas = Servico.Linhas(buscas);
            var saida = Cabecalho("faturamento_por_cliente", buscas, "produtos/nfconsultar", "ListarNF", param, NotasValidas, conferida: false,
                DefinicaoNotas + " Faturamento = soma do valor total da nota (total.ICMSTot.vNF, com impostos e frete). " +
                "Clientes agrupados pelo CNPJ/CPF do destinatário, então o mesmo cliente nas duas empresas soma junto.");
            saida["resumo"] = Resumo.Resumir(linhas, new PedidoResumo(
                Filtros: NotasValidas, Somar: ["total.ICMSTot.vNF"], AgruparPor: "nfDestInt.cnpj_cpf",
                Rotulo: "nfDestInt.cRazao", Top: Math.Clamp(top, 1, 500)));
            return Finalizar(saida, buscas);
        });

    [McpServerTool(Name = "contas_receber_vencidas", Title = "Contas a receber vencidas por cliente", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Overdue receivables (contas a receber em atraso) today, total and per client, with the amount still open. Use for \"quem está devendo\", \"inadimplência\", \"vencidos\".")]
    public static Task<string> ContasReceberVencidas(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("How many clients (default 30).")] int top = 30,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            var hoje = Hoje();
            var param = new JsonObject
            {
                ["cTpLancamento"] = "CR",
                ["cStatus"] = "ATRASADO",
                // without a date filter Omie returns only the last 30 days
                ["dDtVencDe"] = "01/01/2000",
                ["dDtVencAte"] = hoje,
            };
            Filtro[] filtros = [new("detalhes.cStatus", "=", "ATRASADO")];
            return await PorClienteAsync(servico, empresa, "contas_receber_vencidas", param, filtros, top, ordenarPorData: false,
                $"Títulos de contas a receber com status ATRASADO no Omie hoje ({hoje}), vencidos desde 2000. Valor = " +
                "resumo.nValAberto (o que falta receber, já descontado o que foi pago em parte). Agrupado por cliente.", ct);
        });

    [McpServerTool(Name = "contas_pagar_proximos_dias", Title = "Contas a pagar nos próximos dias", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Open payables (contas a pagar em aberto) due in the next N days (default 30), total, per supplier and per day. Use for \"o que temos para pagar\", \"contas a pagar do mês\".")]
    public static Task<string> ContasPagarProximosDias(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("Days ahead, counting today (default 30).")] int dias = 30,
        [Description("How many suppliers (default 30).")] int top = 30,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            dias = Math.Clamp(dias, 1, 366);
            var (de, ate) = (Hoje(), Hoje(dias - 1));
            var param = new JsonObject
            {
                ["cTpLancamento"] = "CP",
                ["cStatus"] = "EMABERTO",
                ["dDtPrevDe"] = de,
                ["dDtPrevAte"] = ate,
            };
            Filtro[] filtros = [new("detalhes.dDtPrevisao", ">=", de), new("detalhes.dDtPrevisao", "<=", ate)];
            return await PorClienteAsync(servico, empresa, "contas_pagar_proximos_dias", param, filtros, top, ordenarPorData: true,
                $"Títulos de contas a pagar com status EM ABERTO e previsão de pagamento (detalhes.dDtPrevisao) entre {de} " +
                $"e {ate}. Valor = resumo.nValAberto. Contas já vencidas e não pagas, com previsão antes de hoje, não entram.", ct);
        });

    private static (List<Busca>, JsonObject) BuscarNotas(Servico servico, string empresa, ref string de, ref string ate)
    {
        (de, ate) = (Servico.Data(de, "de"), Servico.Data(ate, "ate"));
        var param = new JsonObject { ["dEmiInicial"] = de, ["dEmiFinal"] = ate, ["tpNF"] = "1" };
        return (servico.Iniciar(servico.Empresas.Resolver(empresa), "produtos/nfconsultar", "ListarNF", param), param);
    }

    /// <summary>
    /// Financial titles grouped by client or supplier. The movimentos only carry the client code, so the client
    /// register is read alongside (another method on the same key, so it runs at the same time) to put names and
    /// CNPJs on the codes. Grouping is by CNPJ, so with both companies the same client adds up together.
    /// </summary>
    private static async Task<JsonObject> PorClienteAsync(Servico servico, string empresa, string receita, JsonObject param,
                                                          Filtro[] filtros, int top, bool ordenarPorData, string definicao,
                                                          CancellationToken ct)
    {
        var alvo = servico.Empresas.Resolver(empresa);
        var titulos = servico.Iniciar(alvo, "financas/mf", "ListarMovimentos", param);
        var cadastros = servico.Iniciar(alvo, "geral/clientes", "ListarClientes", new JsonObject { ["apenas_importado_api"] = "N" });
        if (!await servico.AguardarAsync([.. titulos, .. cadastros], ct))
            return Servico.Andamento([.. titulos, .. cadastros]);

        var linhas = new List<JsonObject>();
        for (var i = 0; i < alvo.Count; i++)
        {
            var nomes = new Dictionary<string, (string Chave, string Nome)>();
            foreach (var c in cadastros[i].Tarefa.Result)
            {
                var codigo = c["codigo_cliente_omie"]?.ToString();
                if (codigo is null) continue;
                var cnpj = c["cnpj_cpf"]?.ToString() ?? "";
                var nome = c["nome_fantasia"]?.ToString() is { Length: > 0 } f ? f : c["razao_social"]?.ToString() ?? "";
                // government payees (DAS, folha, impostos) share the placeholder 00.000.000/0000-00, so a CNPJ made
                // only of zeros can't be the grouping key: they would all merge under the first name
                var semCnpj = !cnpj.Any(char.IsAsciiDigit) || cnpj.All(ch => ch is '0' || !char.IsAsciiDigit(ch));
                nomes[codigo] = (semCnpj ? $"código {codigo} ({alvo[i].Nome})" : cnpj, nome);
            }
            foreach (var t in titulos[i].Tarefa.Result)
            {
                var linha = (JsonObject)t.DeepClone();
                var codigo = Resumo.Valor(linha, "detalhes.nCodCliente")?.ToString() ?? "";
                var (chave, nome) = nomes.TryGetValue(codigo, out var n) ? n : ($"código {codigo} ({alvo[i].Nome})", "(sem cadastro)");
                linha["_empresa"] = alvo[i].Nome;
                linha["_cliente"] = chave;
                linha["_cliente_nome"] = nome;
                linhas.Add(linha);
            }
        }

        var saida = Cabecalho(receita, titulos, "financas/mf", "ListarMovimentos", param, filtros, conferida: false, definicao);
        saida["resumo"] = Resumo.Resumir(linhas, new PedidoResumo(
            Filtros: filtros, Somar: ["resumo.nValAberto"], AgruparPor: "_cliente", Rotulo: "_cliente_nome", Top: Math.Clamp(top, 1, 500)));
        if (ordenarPorData)
            saida["por_dia"] = Resumo.Resumir(linhas, new PedidoResumo(
                Filtros: filtros, Somar: ["resumo.nValAberto"], AgruparPor: "detalhes.dDtPrevisao", Periodo: "dia", OrdenarPor: "grupo", Top: 400));
        if (alvo.Count > 1)
            saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: filtros, Somar: ["resumo.nValAberto"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
        return Finalizar(saida, [.. titulos, .. cadastros]);
    }

    private static JsonObject Cabecalho(string receita, List<Busca> buscas, string endpoint, string metodo, JsonObject param,
                                        Filtro[] filtros, bool conferida, string definicao)
    {
        var saida = new JsonObject
        {
            ["receita"] = receita,
            ["empresa"] = string.Join(" + ", buscas.Select(b => b.Empresa)),
            ["definicao"] = definicao,
            ["consulta"] = new JsonObject
            {
                ["endpoint"] = endpoint,
                ["metodo"] = metodo,
                ["parametros"] = param.DeepClone(),
                ["filtros_aplicados"] = new JsonArray(filtros.Select(f => (JsonNode)$"{f.Campo} {f.Operador} {f.Valor?.ToString()}".Trim()).ToArray()),
            },
            ["conferida_com_tela_do_omie"] = conferida,
        };
        if (!conferida) saida["aviso_conferencia"] = Pendente;
        return saida;
    }

    private static JsonObject Finalizar(JsonObject saida, List<Busca> buscas)
    {
        var avisos = Servico.Avisos(buscas);
        if (avisos.Count > 0) saida["avisos"] = avisos;
        saida["tempo_de_busca_s"] = (int)buscas.Max(b => b.Tempo.TotalSeconds);
        return saida;
    }

    /// <summary>
    /// Quantity is in each product's commercial unit (MI is milheiro, so 2.6 means 2,600 pieces),
    /// so every product carries its unit and the price is per that unit. A product sold in more than one unit lists
    /// all of them, and its quantity and price mix them.
    /// </summary>
    private static void AdicionarPrecoMedioEUnidade(JsonObject resumo, List<JsonObject> notas)
    {
        if (resumo["grupos"] is not JsonArray grupos) return;
        var unidades = new Dictionary<string, SortedSet<string>>();
        foreach (var item in notas.SelectMany(n => n["det"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var codigo = Resumo.Valor(item, "prod.cProd")?.ToString() ?? "(vazio)";
            var unidade = Resumo.Valor(item, "prod.uCom")?.ToString() is { Length: > 0 } u ? u : "?";
            (unidades.TryGetValue(codigo, out var s) ? s : unidades[codigo] = []).Add(unidade);
        }
        foreach (var g in grupos.OfType<JsonObject>())
        {
            var somas = g["somas"]!.AsObject();
            var receita = somas["det.prod.vProd"]!.GetValue<decimal>();
            var quantidade = somas["det.prod.qCom"]!.GetValue<decimal>();
            var codigo = g["grupo"]!.GetValue<string>();
            g["unidade"] = unidades.TryGetValue(codigo, out var s) ? string.Join(" + ", s) : null;
            g["preco_medio"] = quantidade == 0 ? null : Math.Round(receita / quantidade, 4);
        }
    }

    private static string Hoje(int dias = 0) =>
        DateOnly.FromDateTime(DateTime.Now).AddDays(dias).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
