using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace OmieMcpReadonly.Tests;

public class FerramentasTests
{
    /// <summary>A fake ListarClientes with <paramref name="total"/> records, 100 per page, cadastro-style paging.</summary>
    private static Func<string, JsonObject, (HttpStatusCode, string)> Clientes(int total) => (_, corpo) =>
    {
        var p = corpo["param"]![0]!;
        var pagina = p["pagina"]!.GetValue<int>();
        var tamanho = p["registros_por_pagina"]!.GetValue<int>();
        var paginas = (total + tamanho - 1) / tamanho;
        var itens = Enumerable.Range((pagina - 1) * tamanho, Math.Max(0, Math.Min(tamanho, total - (pagina - 1) * tamanho)))
            .Select(i => (JsonNode)new JsonObject
            {
                ["codigo_cliente_omie"] = i,
                ["razao_social"] = $"Cliente {i}",
                ["info"] = new JsonObject { ["dInc"] = $"{1 + i % 28:00}/{1 + i % 12:00}/2026" },
                ["valor"] = 1.10m,
            }).ToArray();
        return Ambiente.Ok(new JsonObject
        {
            ["pagina"] = pagina,
            ["total_de_paginas"] = paginas,
            ["registros"] = itens.Length,
            ["total_de_registros"] = total,
            ["clientes_cadastro"] = new JsonArray(itens),
        });
    };

    [Fact]
    public async Task Consultar_devolve_no_maximo_200_e_o_total_real()
    {
        using var amb = new Ambiente(Clientes(1234));
        var texto = await Ferramentas.Consultar(amb.Servico, "matriz", "geral/clientes", "ListarClientes", new JsonObject());
        var r = JsonNode.Parse(texto)!;

        Assert.Equal(1234, r["total_de_registros"]!.GetValue<int>());
        Assert.Equal(200, r["linhas_devolvidas"]!.GetValue<int>());
        Assert.Equal(200, r["linhas"]!.AsArray().Count);
        Assert.Contains("use resumir", r["nota"]!.GetValue<string>());
        // stops paging once it has 200 rows
        Assert.Equal(2, amb.Omie.Pedidos.Count);
    }

    [Fact]
    public async Task Consultar_aceita_parametros_como_texto_e_recorta_campos()
    {
        using var amb = new Ambiente(Clientes(3));
        var texto = await Ferramentas.Consultar(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes",
            JsonValue.Create("{\"filtrar_apenas_inclusao\":\"S\"}"), ["razao_social", "info.dInc"]);
        var r = JsonNode.Parse(texto)!;
        var primeira = r["linhas"]![0]!.AsObject();
        Assert.Equal(["razao_social", "info.dInc"], primeira.Select(p => p.Key));
        Assert.Equal("S", amb.Omie.Pedidos[0].Corpo["param"]![0]!["filtrar_apenas_inclusao"]!.GetValue<string>());
    }

    [Fact]
    public async Task Resumir_le_todas_as_paginas_e_soma_exato()
    {
        using var amb = new Ambiente(Clientes(1234));
        var texto = await Ferramentas.Resumir(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes", null,
            somar: ["valor"], agrupar_por: "info.dInc", periodo: "mes", ordenar_por: "grupo", top: 12);
        var r = JsonNode.Parse(texto)!["resumo"]!;

        Assert.Equal(1234, r["linhas_consideradas"]!.GetValue<int>());
        Assert.Equal(1357.40m, r["somas"]!["valor"]!.GetValue<decimal>());
        Assert.Equal(12, r["total_de_grupos"]!.GetValue<int>());
        Assert.Equal("2026-01", r["grupos"]![0]!["grupo"]!.GetValue<string>());
        Assert.Equal(13, amb.Omie.Pedidos.Count);
    }

    [Fact]
    public async Task Segunda_pergunta_igual_usa_o_cache()
    {
        using var amb = new Ambiente(Clientes(250));
        await Ferramentas.Resumir(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes", null);
        await Ferramentas.Consultar(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes", null);
        Assert.Equal(3, amb.Omie.Pedidos.Count);
    }

    [Fact]
    public async Task Ambas_soma_as_empresas_e_separa_por_empresa()
    {
        using var amb = new Ambiente(Clientes(150), null, "MATRIZ", "FILIAL");
        var texto = await Ferramentas.Resumir(amb.Servico, "ambas", "geral/clientes", "ListarClientes", null, somar: ["valor"]);
        var r = JsonNode.Parse(texto)!;

        Assert.Equal("MATRIZ + FILIAL", r["empresa"]!.GetValue<string>());
        Assert.Equal(300, r["resumo"]!["linhas_consideradas"]!.GetValue<int>());
        Assert.Equal(150, r["por_empresa"]!["FILIAL"]!["linhas_consideradas"]!.GetValue<int>());
        Assert.Contains(amb.Omie.Pedidos, p => p.Corpo["app_key"]!.GetValue<string>() == "key-FILIAL");
    }

    [Fact]
    public async Task Sem_empresa_com_duas_configuradas_pede_para_perguntar()
    {
        using var amb = new Ambiente(Clientes(1), null, "MATRIZ", "FILIAL");
        var erro = await Assert.ThrowsAsync<McpException>(() =>
            Ferramentas.Consultar(amb.Servico, "", "geral/clientes", "ListarClientes", null));
        Assert.Contains("Pergunte ao usuário", erro.Message);
        Assert.Empty(amb.Omie.Pedidos);
    }

    [Fact]
    public async Task Busca_longa_responde_andamento_e_continua_na_proxima_chamada()
    {
        using var amb = new Ambiente(Clientes(500), TimeSpan.FromMilliseconds(150));
        amb.Omie.Atraso = TimeSpan.FromMilliseconds(100);

        var primeira = JsonNode.Parse(await Ferramentas.Resumir(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes", null))!;
        Assert.Equal("ainda buscando no Omie", primeira["situacao"]!.GetValue<string>());

        JsonNode? r = primeira;
        for (var i = 0; i < 50 && r!["situacao"] is not null; i++)
            r = JsonNode.Parse(await Ferramentas.Resumir(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes", null));

        Assert.Equal(500, r!["resumo"]!["linhas_consideradas"]!.GetValue<int>());
        // the job carried on between calls: each page was asked once
        Assert.Equal(5, amb.Omie.Pedidos.Count);
    }

    [Fact]
    public void Paginacao_estilo_n_no_financeiro()
    {
        Assert.Equal(("nPagina", "nRegPorPagina"), Buscador.EstiloPaginacao("financas/mf", []));
        Assert.Equal(("pagina", "registros_por_pagina"), Buscador.EstiloPaginacao("geral/clientes", []));
        Assert.Equal(("nPagina", "nRegPorPagina"), Buscador.EstiloPaginacao("outro/servico", new JsonObject { ["nPagina"] = 1 }));
    }

    [Fact]
    public async Task Metodo_Consultar_nao_pagina()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok(new JsonObject { ["codigo_cliente_omie"] = 7, ["razao_social"] = "X" }));
        var r = JsonNode.Parse(await Ferramentas.Consultar(amb.Servico, "MATRIZ", "geral/clientes", "ConsultarCliente",
            new JsonObject { ["codigo_cliente_omie"] = 7 }))!;
        Assert.Equal(1, r["total_de_registros"]!.GetValue<int>());
        Assert.Null(amb.Omie.Pedidos[0].Corpo["param"]![0]!["pagina"]);
    }

    [Fact]
    public void Empresas_lidas_do_ambiente()
    {
        var env = new System.Collections.Hashtable
        {
            ["OMIE_EMPRESA_1_NOME"] = "Matriz", ["OMIE_EMPRESA_1_APP_KEY"] = "k1", ["OMIE_EMPRESA_1_APP_SECRET"] = "s1",
            ["OMIE_EMPRESA_2_NOME"] = "${user_config.empresa_2_nome}", ["OMIE_EMPRESA_2_APP_KEY"] = "", ["OMIE_EMPRESA_2_APP_SECRET"] = "",
            ["OMIE_EMPRESA_3_NOME"] = "Filial", ["OMIE_EMPRESA_3_APP_KEY"] = "k3", ["OMIE_EMPRESA_3_APP_SECRET"] = "s3",
        };
        var e = Empresas.DoAmbiente(env);
        Assert.Equal("MATRIZ, FILIAL", e.Nomes);
        Assert.Equal("FILIAL", e.Resolver("filial").Single().Nome);
        Assert.Equal(2, e.Resolver("Ambas").Count);
    }

    [Fact]
    public void Log_apaga_arquivos_com_mais_de_30_dias()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok([]));
        File.WriteAllText(Path.Combine(amb.PastaLog, "chamadas-2026-09-01.jsonl"), "{}");
        File.WriteAllText(Path.Combine(amb.PastaLog, "chamadas-2026-10-01.jsonl"), "{}");
        amb.Log.ApagarAntigos(new DateTime(2026, 10, 22));
        Assert.Equal(["chamadas-2026-10-01.jsonl"], Directory.GetFiles(amb.PastaLog).Select(Path.GetFileName));
    }
}
