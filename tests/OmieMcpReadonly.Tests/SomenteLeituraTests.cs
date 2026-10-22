using System.Net;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace OmieMcpReadonly.Tests;

/// <summary>The read-only promise: a write never leaves the PC, whichever way it is asked.</summary>
public class SomenteLeituraTests
{
    [Theory]
    [InlineData("ListarClientes")]
    [InlineData("PesquisarLancamentos")]
    [InlineData("ConsultarCliente")]
    [InlineData("ObterEstoqueProduto")]
    [InlineData("ListarNF")]
    public void Aceita_metodos_de_leitura(string metodo) =>
        Assert.Null(ReadOnlyGuard.Recusa("geral/clientes", metodo));

    [Theory]
    [InlineData("IncluirCliente")]
    [InlineData("AlterarCliente")]
    [InlineData("ExcluirCliente")]
    [InlineData("UpsertCliente")]
    [InlineData("LancarPagamento")]
    [InlineData("CancelarPedido")]
    [InlineData("listarClientes")]
    [InlineData("Listar")]
    [InlineData("Listarclientes")]
    [InlineData("Listar Clientes")]
    [InlineData("ListarClientes\",\"call\":\"IncluirCliente")]
    [InlineData("ListarClientes/../IncluirCliente")]
    [InlineData("")]
    public void Recusa_o_que_nao_e_leitura(string metodo)
    {
        var recusa = ReadOnlyGuard.Recusa("geral/clientes", metodo);
        Assert.NotNull(recusa);
        Assert.StartsWith("Recusado", recusa);
    }

    [Theory]
    [InlineData("../geral/clientes")]
    [InlineData("https://outro.site/api")]
    [InlineData("geral/clientes?x=1")]
    [InlineData("geral")]
    public void Recusa_endpoint_fora_do_formato(string endpoint) =>
        Assert.NotNull(ReadOnlyGuard.Recusa(endpoint, "ListarClientes"));

    [Fact]
    public void Aceita_endpoint_com_barras_e_maiusculas() =>
        Assert.Null(ReadOnlyGuard.Recusa("/Geral/Clientes/", "ListarClientes"));

    [Fact]
    public async Task IncluirCliente_nunca_chega_a_rede()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok([]));

        await Assert.ThrowsAsync<ReadOnlyViolationException>(() =>
            amb.Cliente.ChamarAsync("geral/clientes", "IncluirCliente", new JsonObject { ["razao_social"] = "Teste" }));

        Assert.Empty(amb.Omie.Pedidos);
        var linha = Assert.Single(amb.LinhasDoLog());
        Assert.Contains("\"resultado\":\"recusado\"", linha);
        Assert.Contains("IncluirCliente", linha);
    }

    [Fact]
    public async Task Ferramenta_consultar_recusa_escrita_com_mensagem_clara()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok([]));

        var erro = await Assert.ThrowsAsync<McpException>(() => Ferramentas.Consultar(
            amb.Servico, "MATRIZ", "geral/clientes", "IncluirCliente", new JsonObject { ["razao_social"] = "Teste" }));

        Assert.Contains("não é de leitura", erro.Message);
        Assert.Empty(amb.Omie.Pedidos);
        Assert.Contains("\"resultado\":\"recusado\"", Assert.Single(amb.LinhasDoLog()));
    }

    [Fact]
    public async Task Ferramenta_resumir_recusa_exclusao()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok([]));

        await Assert.ThrowsAsync<McpException>(() => Ferramentas.Resumir(
            amb.Servico, "MATRIZ", "financas/contapagar", "ExcluirContaPagar", new JsonObject { ["codigo_lancamento_omie"] = 1 }));

        Assert.Empty(amb.Omie.Pedidos);
    }

    [Fact]
    public void So_o_OmieClient_fala_com_a_rede()
    {
        // the guard sits in OmieClient.ChamarAsync; any other HTTP call in the code would be a way around it
        var fontes = Directory.EnumerateFiles(Path.Combine(Raiz(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"\.(SendAsync|PostAsync|GetAsync|PutAsync|DeleteAsync|PatchAsync)\("))
            .Select(Path.GetFileName);
        Assert.Equal(["OmieClient.cs"], fontes);
    }

    [Fact]
    public void Chaves_nunca_vao_para_o_log()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok(new JsonObject { ["x"] = 1 }));
        amb.Cliente.ChamarAsync("geral/empresas", "ListarEmpresas", []).GetAwaiter().GetResult();
        var texto = string.Join("\n", amb.LinhasDoLog());
        Assert.DoesNotContain("key-MATRIZ", texto);
        Assert.DoesNotContain("secret-MATRIZ", texto);
        Assert.Contains("ListarEmpresas", texto);
    }

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "omie-mcp-readonly.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("raiz do repositório");
    }
}
