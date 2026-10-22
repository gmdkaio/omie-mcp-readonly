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

    // Billing follows Omie's own "Faturamento por Período" report in the view it opens with: Operação = Pedido de
    // Venda and Situação = Autorizado. That view matched a management DRE to the cent (22/10/2026). The operation and
    // the order's status come in the "pedido" block, which ListarNF only fills with cDetalhesPedido = S. Remessas
    // (samples, moulds, conserto, the remessa of an entrega-futura sale) are opPedido 14, so they drop out, and so
    // does an invoice Omie shows as rejected, which the API has no flag for but which was a remessa
    private static readonly Filtro[] NotasDeVenda =
    [
        new("ide.tpNF", "=", "1"),
        new("ide.finNFe", "!=", "4"),
        new("ide.dCan", "vazio"),
        new("ide.dInut", "vazio"),
        new("ide.cDeneg", "!=", "S"),
        // 11 Venda de Produto, 12 Venda de Produto pelo PDV
        new("pedido.opPedido", "em", new JsonArray("11", "12")),
        new("pedido.cCancelado", "!=", "S"),
        new("pedido.cDevolvido", "!=", "S"),
        new("pedido.cDevParcial", "!=", "S"),
    ];

    // customer returns are incoming invoices (tpNF 0) whose purpose is devolução (finNFe 4), usually CFOP 1.201/2.201.
    // Omie's report default view doesn't subtract them, so the headline doesn't either; they come apart, with the net
    // figure computed here so Claude never does the subtraction itself
    private static readonly Filtro[] Devolucoes =
    [
        new("ide.tpNF", "=", "0"),
        new("ide.finNFe", "=", "4"),
        new("ide.dCan", "vazio"),
        new("ide.dInut", "vazio"),
        new("ide.cDeneg", "!=", "S"),
    ];

    private const string DefinicaoNotas =
        "Mesma regra do relatório Faturamento por Período do Omie na visão padrão (Operação = Pedido de Venda, " +
        "Situação = Autorizado): NF-e de saída emitidas no período (ide.dEmi) que vieram de um pedido de venda de " +
        "produto, sem as canceladas, inutilizadas e denegadas, e sem as notas de pedidos devolvidos, mesmo que em parte. " +
        "Remessas (amostras, moldes, conserto, entrega futura) não entram. O número principal não desconta devoluções de " +
        "cliente, como no relatório; elas vêm à parte em devolucoes (notas de entrada de devolução emitidas no período), " +
        "e o valor já descontado está em *_menos_devolucoes.";

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

            // the date filter runs again here: Omie's filter limits what is downloaded, this one is what gets counted
            Filtro[] noPeriodo = [new("info.dInc", ">=", de), new("info.dInc", "<=", ate)];
            // the client register also holds suppliers, carriers, staff and cadastros with no tag: a new client is
            // one with the tag Cliente. Unnesting tags gives one row per tag, and a cadastro has Cliente at most once
            Filtro[] clientes = [.. noPeriodo, new("tags.tag", "=", "Cliente")];
            var linhas = Servico.Linhas(buscas);
            var saida = Cabecalho("clientes_novos", buscas, "geral/clientes", "ListarClientes", param, clientes, conferida: true,
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
            var (buscas, devolucoes, param) = BuscarNotas(servico, empresa, ref de, ref ate);
            if (!await servico.AguardarAsync([.. buscas, .. devolucoes], ct))
                return Servico.Andamento([.. buscas, .. devolucoes]);

            var linhas = Servico.Linhas(buscas);
            var saida = Cabecalho("produtos_mais_vendidos", buscas, "produtos/nfconsultar", "ListarNF", param, NotasDeVenda, conferida: true,
                DefinicaoNotas + " Uma linha por item de nota (det). Receita = soma de det.prod.vProd (valor bruto dos " +
                "produtos, sem IPI, ST e frete, antes de descontos); quantidade = soma de det.prod.qCom na unidade " +
                "comercial de cada produto (campo unidade; MI = milheiro, então 2,6 MI são 2.600 peças); preço médio = " +
                "receita / quantidade, por essa unidade. Ao ordenar por quantidade, produtos em unidades diferentes " +
                "não são comparáveis entre si: diga a unidade junto de cada quantidade. A soma geral de quantidade " +
                "mistura unidades e não deve ser citada.");
            var resumo = Resumo.Resumir(linhas, new PedidoResumo(
                Expandir: "det", Filtros: NotasDeVenda, Somar: ["det.prod.vProd", "det.prod.qCom"],
                AgruparPor: "det.prod.cProd", Rotulo: "det.prod.xProd", OrdenarPor: ordem, Top: Math.Clamp(top, 1, 500)));
            AdicionarPrecoMedioEUnidade(resumo, linhas);
            saida["resumo"] = resumo;
            var devolvidos = Resumo.Resumir(Servico.Linhas(devolucoes), new PedidoResumo(
                Expandir: "det", Filtros: Devolucoes, Somar: ["det.prod.vProd", "det.prod.qCom"],
                AgruparPor: "det.prod.cProd", Rotulo: "det.prod.xProd", Top: 20));
            saida["devolucoes"] = devolvidos;
            saida["receita_menos_devolucoes"] = Soma(resumo, "det.prod.vProd") - Soma(devolvidos, "det.prod.vProd");
            if (buscas.Count > 1)
                saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(
                    Expandir: "det", Filtros: NotasDeVenda, Somar: ["det.prod.vProd"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
            return Finalizar(saida, [.. buscas, .. devolucoes]);
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
            var (buscas, devolucoes, param) = BuscarNotas(servico, empresa, ref de, ref ate);
            if (!await servico.AguardarAsync([.. buscas, .. devolucoes], ct))
                return Servico.Andamento([.. buscas, .. devolucoes]);

            var linhas = Servico.Linhas(buscas);
            var saida = Cabecalho("faturamento_por_cliente", buscas, "produtos/nfconsultar", "ListarNF", param, NotasDeVenda, conferida: true,
                DefinicaoNotas + " Faturamento = soma do valor total da nota (total.ICMSTot.vNF, com impostos e frete). " +
                "Clientes agrupados pelo CNPJ/CPF do destinatário, então o mesmo cliente nas duas empresas soma junto.");
            saida["resumo"] = Resumo.Resumir(linhas, new PedidoResumo(
                Filtros: NotasDeVenda, Somar: ["total.ICMSTot.vNF"], AgruparPor: "nfDestInt.cnpj_cpf",
                Rotulo: "nfDestInt.cRazao", Top: Math.Clamp(top, 1, 500)));
            var devolvidas = Resumo.Resumir(Servico.Linhas(devolucoes), new PedidoResumo(
                Filtros: Devolucoes, Somar: ["total.ICMSTot.vNF"], AgruparPor: "nfDestInt.cnpj_cpf", Rotulo: "nfDestInt.cRazao", Top: 20));
            saida["devolucoes"] = devolvidas;
            saida["faturamento_menos_devolucoes"] = Soma(saida["resumo"]!.AsObject(), "total.ICMSTot.vNF") - Soma(devolvidas, "total.ICMSTot.vNF");
            if (buscas.Count > 1)
            {
                saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(
                    Filtros: NotasDeVenda, Somar: ["total.ICMSTot.vNF"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
                saida["devolucoes_por_empresa"] = Resumo.Resumir(Servico.Linhas(devolucoes), new PedidoResumo(
                    Filtros: Devolucoes, Somar: ["total.ICMSTot.vNF"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
            }
            return Finalizar(saida, [.. buscas, .. devolucoes]);
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
            // a title received in part comes back with status RECEBIDO but cLiquidado N and the rest still open, and
            // Omie's own "Contas a Receber em Atraso" screen counts it; so the status text is not the test, the open
            // balance is (checked against that screen on 22/10/2026)
            Filtro[] filtros =
            [
                new("resumo.cLiquidado", "!=", "S"),
                new("resumo.nValAberto", ">", 0),
                new("detalhes.dDtVenc", "<", hoje),
            ];
            return await PorClienteAsync(servico, empresa, "contas_receber_vencidas", param, filtros, top, ordenarPorData: false, conferida: true,
                $"Títulos de contas a receber vencidos antes de hoje ({hoje}), não liquidados e com valor em aberto, inclusive " +
                "os recebidos em parte. Valor = resumo.nValAberto (o que falta receber, já descontado o que foi pago). " +
                "Agrupado por cliente. A tela de atrasados do Omie pode deixar de fora um título vencido ontem.", ct);
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
            return await PorClienteAsync(servico, empresa, "contas_pagar_proximos_dias", param, filtros, top, ordenarPorData: true, conferida: true,
                $"Títulos de contas a pagar com status EM ABERTO e previsão de pagamento (detalhes.dDtPrevisao) entre {de} " +
                $"e {ate}. Valor = resumo.nValAberto. Contas já vencidas e não pagas, com previsão antes de hoje, não entram.", ct);
        });

    [McpServerTool(Name = "faturamento_por_vendedor", Title = "Faturamento por vendedor", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Billing per seller in a period, from issued invoices (same rule as faturamento_por_cliente), with the seller taken from the sales order. Sellers are matched by name across companies. Use for \"quem vendeu mais\", \"vendas por vendedor\".")]
    public static Task<string> FaturamentoPorVendedor(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("Start date dd/mm/aaaa (emission).")] string de,
        [Description("End date dd/mm/aaaa (emission).")] string ate,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            (de, ate) = (Servico.Data(de, "de"), Servico.Data(ate, "ate"));
            var alvo = servico.Empresas.Resolver(empresa);
            var param = ParamNotas(de, ate);
            var notas = servico.Iniciar(alvo, "produtos/nfconsultar", "ListarNF", param);
            var vendedores = servico.Iniciar(alvo, "geral/vendedores", "ListarVendedores", new JsonObject { ["apenas_importado_api"] = "N" });
            if (!await servico.AguardarAsync([.. notas, .. vendedores], ct))
                return Servico.Andamento([.. notas, .. vendedores]);

            // each company has its own seller codes, so the same person is matched by name
            var linhas = new List<JsonObject>();
            for (var i = 0; i < alvo.Count; i++)
            {
                var nomes = vendedores[i].Tarefa.Result
                    .Where(v => v["codigo"] is not null)
                    .GroupBy(v => v["codigo"]!.ToString())
                    .ToDictionary(g => g.Key, g => g.First()["nome"]?.ToString() ?? g.Key);
                foreach (var n in notas[i].Tarefa.Result)
                {
                    var linha = (JsonObject)n.DeepClone();
                    var codigo = Resumo.Valor(linha, "pedido.nIdVendedor")?.ToString();
                    var nome = codigo is null || codigo == "0" ? "(sem vendedor)" : nomes.GetValueOrDefault(codigo, $"código {codigo}");
                    linha["_empresa"] = alvo[i].Nome;
                    linha["_vendedor"] = nome;
                    linha["_vendedor_empresa"] = $"{nome} ({alvo[i].Nome})";
                    linhas.Add(linha);
                }
            }
            var saida = Cabecalho("faturamento_por_vendedor", notas, "produtos/nfconsultar", "ListarNF", param, NotasDeVenda, conferida: true,
                DefinicaoNotas + " Faturamento = soma do valor total da nota (total.ICMSTot.vNF). Vendedor = o do pedido de " +
                "venda que originou a nota (pedido.nIdVendedor), pelo nome do cadastro de vendedores de cada empresa.");
            saida["resumo"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: NotasDeVenda, Somar: ["total.ICMSTot.vNF"], AgruparPor: "_vendedor"));
            if (alvo.Count > 1)
            {
                saida["por_vendedor_e_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: NotasDeVenda, Somar: ["total.ICMSTot.vNF"], AgruparPor: "_vendedor_empresa"));
                saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: NotasDeVenda, Somar: ["total.ICMSTot.vNF"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
            }
            return Finalizar(saida, [.. notas, .. vendedores]);
        });

    [McpServerTool(Name = "despesas_por_categoria", Title = "Despesas por categoria", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Spending per category in a period, cash basis: what actually left the company's accounts (bank, card, cash), from the bank statements, with transfers between its own accounts left out. Use for \"quanto gastamos por categoria\", \"maiores despesas\", \"para onde foi o dinheiro\". Paid bills alone (financas/mf) miss the entries made straight in the account, such as pró-labore.")]
    public static Task<string> DespesasPorCategoria(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        [Description("Start date dd/mm/aaaa.")] string de,
        [Description("End date dd/mm/aaaa.")] string ate,
        [Description("How many categories (default 30).")] int top = 30,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            (de, ate) = (Servico.Data(de, "de"), Servico.Data(ate, "ate"));
            var alvo = servico.Empresas.Resolver(empresa);
            var contas = servico.Iniciar(alvo, "geral/contacorrente", "ListarResumoContasCorrentes", new JsonObject());
            if (!await servico.AguardarAsync(contas, ct))
                return Servico.Andamento(contas);

            // every account except the advance controls (type AD), which only mirror amounts held for or by others
            var extratos = new List<(Empresa Empresa, string Conta, Busca Busca)>();
            for (var i = 0; i < alvo.Count; i++)
                foreach (var c in contas[i].Tarefa.Result.Where(c => c["tipo"]?.ToString() != "AD" && c["nCodCC"] is not null))
                {
                    var param = new JsonObject { ["nCodCC"] = c["nCodCC"]!.DeepClone(), ["dPeriodoInicial"] = de, ["dPeriodoFinal"] = ate };
                    extratos.Add((alvo[i], c["descricao"]?.ToString() ?? c["nCodCC"]!.ToString(), servico.Iniciar([alvo[i]], "financas/extrato", "ListarExtrato", param)[0]));
                }
            var buscas = extratos.Select(e => e.Busca).ToList();
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento([.. contas, .. buscas]);

            var linhas = new List<JsonObject>();
            foreach (var (emp, conta, busca) in extratos)
                foreach (var m in busca.Tarefa.Result)
                {
                    var linha = (JsonObject)m.DeepClone();
                    linha["_empresa"] = emp.Nome;
                    linha["_conta"] = $"{conta} ({emp.Nome})";
                    linha["_saida"] = -(Resumo.Numero(linha["nValorDocumento"]) ?? 0m);
                    linhas.Add(linha);
                }
            // an outflow that isn't a transfer to another of the company's own accounts (paying the card bill from the
            // bank is a transfer; the card's purchases are counted on the card account)
            Filtro[] saidas = [new("nValorDocumento", "<", 0), new("cOrigem", "nao_contem", "Transfer")];
            var saida = Cabecalho("despesas_por_categoria", contas, "financas/extrato", "ListarExtrato",
                new JsonObject { ["dPeriodoInicial"] = de, ["dPeriodoFinal"] = ate, ["contas"] = new JsonArray(extratos.Select(e => (JsonNode)$"{e.Conta} ({e.Empresa.Nome})").ToArray()) },
                saidas, conferida: false,
                $"Regime de caixa: saídas lançadas no extrato de cada conta entre {de} e {ate} (banco, cartão, caixa), sem as " +
                "transferências entre as contas da própria empresa e sem as contas de adiantamento. Valor = nValorDocumento " +
                "com o sinal invertido; categoria = cDesCategoria do lançamento. Previsões futuras que estejam no extrato entram " +
                "só se já tiverem data no período.");
            saida["resumo"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: saidas, Somar: ["_saida"], AgruparPor: "cDesCategoria", Top: Math.Clamp(top, 1, 500)));
            saida["por_conta"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: saidas, Somar: ["_saida"], AgruparPor: "_conta"));
            if (alvo.Count > 1)
                saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: saidas, Somar: ["_saida"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
            return Finalizar(saida, [.. contas, .. buscas]);
        });

    [McpServerTool(Name = "pedidos_em_aberto", Title = "Pedidos de venda em aberto", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sales orders not yet billed, by stage (etapa), with count and value, separating quotes (orçamentos) from firm orders. Use for \"pedidos em aberto\", \"carteira de pedidos\", \"o que falta faturar\".")]
    public static Task<string> PedidosEmAberto(
        Servico servico,
        [Description(Ferramentas.DescricaoEmpresa)] string empresa,
        CancellationToken ct = default) =>
        Ferramentas.Executar(async () =>
        {
            var alvo = servico.Empresas.Resolver(empresa);
            var etapas = servico.Iniciar(alvo, "produtos/etapafat", "ListarEtapasFaturamento", new JsonObject());
            if (!await servico.AguardarAsync(etapas, ct))
                return Servico.Andamento(etapas);

            // stages of operation 11 (Venda de Produto) except 60 Faturado and 70 Entrega; stage 00 holds quotes even when
            // marked inactive. Reading only these stages avoids paging through every order ever made
            var pedidos = new List<(Empresa Empresa, string Etapa, string Nome, Busca Busca)>();
            for (var i = 0; i < alvo.Count; i++)
            {
                var venda = etapas[i].Tarefa.Result.FirstOrDefault(o => o["cCodOperacao"]?.ToString() == "11");
                foreach (var e in (venda?["etapas"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var codigo = e["cCodigo"]?.ToString();
                    if (codigo is null or "60" or "70") continue;
                    var nome = e["cDescricao"]?.ToString() is { Length: > 0 } d && d != "<disponível>" ? d : e["cDescrPadrao"]?.ToString() ?? codigo;
                    var param = new JsonObject { ["etapa"] = codigo, ["apenas_importado_api"] = "N" };
                    pedidos.Add((alvo[i], codigo, nome, servico.Iniciar([alvo[i]], "produtos/pedido", "ListarPedidos", param)[0]));
                }
            }
            var buscas = pedidos.Select(p => p.Busca).ToList();
            if (!await servico.AguardarAsync(buscas, ct))
                return Servico.Andamento([.. etapas, .. buscas]);

            var linhas = new List<JsonObject>();
            foreach (var (emp, codigo, nome, busca) in pedidos)
                foreach (var p in busca.Tarefa.Result)
                {
                    var linha = (JsonObject)p.DeepClone();
                    linha["_empresa"] = emp.Nome;
                    linha["_etapa"] = $"{codigo} {nome}";
                    linha["_tipo"] = codigo == "00" ? "orçamento" : "pedido";
                    linhas.Add(linha);
                }
            Filtro[] abertos =
            [
                new("infoCadastro.cancelado", "!=", "S"),
                new("infoCadastro.faturado", "!=", "S"),
                new("infoCadastro.encerrado", "!=", "S"),
            ];
            var saida = Cabecalho("pedidos_em_aberto", etapas, "produtos/pedido", "ListarPedidos",
                new JsonObject { ["etapas"] = new JsonArray(pedidos.Select(p => (JsonNode)$"{p.Etapa} {p.Nome} ({p.Empresa.Nome})").ToArray()) },
                abertos, conferida: true,
                "Pedidos de venda de produto (operação 11) nas etapas antes de Faturado (60) e Entrega (70), sem os cancelados, " +
                "faturados e encerrados. A etapa 00 é de orçamentos; os pedidos firmes são as outras. Valor = " +
                "total_pedido.valor_total_pedido.");
            saida["resumo"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: abertos, Somar: ["total_pedido.valor_total_pedido"], AgruparPor: "_etapa", OrdenarPor: "grupo"));
            saida["por_tipo"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: abertos, Somar: ["total_pedido.valor_total_pedido"], AgruparPor: "_tipo", OrdenarPor: "grupo"));
            if (alvo.Count > 1)
                saida["por_empresa"] = Resumo.Resumir(linhas, new PedidoResumo(Filtros: abertos, Somar: ["total_pedido.valor_total_pedido"], AgruparPor: "_empresa", OrdenarPor: "grupo"));
            return Finalizar(saida, [.. etapas, .. buscas]);
        });

    private static JsonObject ParamNotas(string de, string ate) =>
        new() { ["dEmiInicial"] = de, ["dEmiFinal"] = ate, ["tpNF"] = "1", ["cDetalhesPedido"] = "S" };
    /// <summary>The issued invoices of the period and, alongside (same method, so one after the other), the customer returns.</summary>
    private static (List<Busca> Notas, List<Busca> Devolucoes, JsonObject Param) BuscarNotas(Servico servico, string empresa, ref string de, ref string ate)
    {
        (de, ate) = (Servico.Data(de, "de"), Servico.Data(ate, "ate"));
        var alvo = servico.Empresas.Resolver(empresa);
        var param = ParamNotas(de, ate);
        var entradas = new JsonObject { ["dEmiInicial"] = de, ["dEmiFinal"] = ate, ["tpNF"] = "0" };
        return (servico.Iniciar(alvo, "produtos/nfconsultar", "ListarNF", param),
                servico.Iniciar(alvo, "produtos/nfconsultar", "ListarNF", entradas), param);
    }

    private static decimal Soma(JsonObject resumo, string campo) => resumo["somas"]?[campo]?.GetValue<decimal>() ?? 0m;

    /// <summary>
    /// Financial titles grouped by client or supplier. The movimentos only carry the client code, so the client
    /// register is read alongside (another method on the same key, so it runs at the same time) to put names and
    /// CNPJs on the codes. Grouping is by CNPJ, so with both companies the same client adds up together.
    /// </summary>
    private static async Task<JsonObject> PorClienteAsync(Servico servico, string empresa, string receita, JsonObject param,
                                                          Filtro[] filtros, int top, bool ordenarPorData, bool conferida, string definicao,
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

        var saida = Cabecalho(receita, titulos, "financas/mf", "ListarMovimentos", param, filtros, conferida, definicao);
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
