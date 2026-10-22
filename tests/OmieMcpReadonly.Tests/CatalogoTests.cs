using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace OmieMcpReadonly.Tests;

/// <summary>The catalog built into the exe, as Claude sees it through the catalogo tool.</summary>
public class CatalogoTests
{
    private static readonly Catalogo Catalogo = Catalogo.Embutido();

    private static List<string> Endpoints(JsonObject r) =>
        r["servicos"]!.AsArray().Select(s => s!["endpoint"]!.GetValue<string>()).ToList();

    [Fact]
    public void Tem_todos_os_servicos_e_so_metodos_de_leitura()
    {
        Assert.True(Catalogo.Dados.Servicos.Count > 100);
        var metodos = Catalogo.Dados.Servicos.SelectMany(s => s.Metodos.Select(m => (s.Endpoint, m.Nome))).ToList();
        Assert.True(metodos.Count > 150);
        Assert.All(metodos, m => Assert.Null(ReadOnlyGuard.Recusa(m.Endpoint, m.Nome)));
        Assert.Contains("IncluirCliente", Catalogo.Dados.Servicos.Single(s => s.Endpoint == "geral/clientes").Recusados);
    }

    [Theory]
    [InlineData("notas fiscais emitidas", "produtos/nfconsultar")]
    [InlineData("posição de estoque", "estoque/consulta")]
    [InlineData("pedidos de venda", "produtos/pedido")]
    [InlineData("contas a receber", "financas/contareceber")]
    [InlineData("oportunidades crm", "crm/oportunidades")]
    [InlineData("ordens de serviço", "servicos/os")]
    public void Busca_acha_o_servico_entre_os_primeiros(string busca, string esperado) =>
        Assert.Contains(esperado, Endpoints(Catalogo.Buscar(busca)).Take(5));

    [Fact]
    public void Detalhe_do_ListarNF_da_os_campos_que_as_receitas_usam()
    {
        var r = Catalogo.Detalhar("produtos/nfconsultar", "ListarNF");
        var m = Assert.Single(r["metodos"]!.AsArray())!;
        var campos = string.Join("\n", m["campos_do_registro"]!.AsArray().Select(c => c!.GetValue<string>()));
        var parametros = string.Join("\n", m["parametros"]!.AsArray().Select(c => c!.GetValue<string>()));

        Assert.Equal("nfCadastro", m["lista_de_registros"]!.GetValue<string>());
        Assert.Contains("det[].prod.vProd (", campos);
        Assert.Contains("det[].prod.uCom (", campos);
        Assert.Contains("ide.dEmi (", campos);
        Assert.Contains("ide.dCan (", campos);
        Assert.Contains("nfDestInt.cnpj_cpf (", campos);
        Assert.Contains("dEmiInicial (", parametros);
        Assert.Contains("tpNF (", parametros);
        // paging is the connector's job, so it isn't offered as a parameter
        Assert.DoesNotContain("registros_por_pagina (", parametros);
    }

    [Fact]
    public void Detalhe_do_ListarMovimentos()
    {
        var m = Catalogo.Detalhar("/financas/mf/", "listarmovimentos")["metodos"]![0]!;
        var campos = string.Join("\n", m["campos_do_registro"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Contains("detalhes.cStatus (", campos);
        Assert.Contains("resumo.nValAberto (", campos);
        Assert.Contains(m["parametros"]!.AsArray(), p => p!.GetValue<string>().StartsWith("cStatus ("));
    }

    [Fact]
    public void Metodo_de_escrita_no_detalhe_e_recusado_com_explicacao()
    {
        var e = Assert.Throws<ArgumentException>(() => Catalogo.Detalhar("geral/clientes", "IncluirCliente"));
        Assert.Contains("não é de leitura", e.Message);
        Assert.Contains("ListarClientes", e.Message);
    }

    [Fact]
    public async Task Ferramenta_sem_argumentos_lista_tudo_e_endpoint_errado_e_erro_claro()
    {
        var indice = JsonNode.Parse(await Ferramentas.ConsultarCatalogo(Catalogo))!.AsObject();
        Assert.Equal(Catalogo.Dados.Servicos.Count, indice["servicos"]!.AsArray().Count);

        var erro = await Assert.ThrowsAsync<McpException>(() => Ferramentas.ConsultarCatalogo(Catalogo, endpoint: "geral/naoexiste"));
        Assert.Contains("busca", erro.Message);
    }
}
