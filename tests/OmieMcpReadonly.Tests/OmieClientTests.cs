using System.Net;
using System.Text.Json.Nodes;

namespace OmieMcpReadonly.Tests;

/// <summary>The Omie rules learned on 20 and 21/10/2026: what is retried, how long it waits, what is not.</summary>
public class OmieClientTests
{
    [Fact]
    public async Task Envia_o_corpo_no_formato_do_Omie()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok(new JsonObject { ["ok"] = true }));
        await amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", new JsonObject { ["pagina"] = 1 });

        var (url, corpo) = Assert.Single(amb.Omie.Pedidos);
        Assert.Equal("https://app.omie.com.br/api/v1/geral/clientes/", url);
        Assert.Equal("ListarClientes", corpo["call"]!.GetValue<string>());
        Assert.Equal("key-MATRIZ", corpo["app_key"]!.GetValue<string>());
        Assert.Equal(1, corpo["param"]![0]!["pagina"]!.GetValue<int>());
        // Omie refuses "application/json; charset=utf-8" with a SOAP Bad Request
        Assert.Equal("application/json", Assert.Single(amb.Omie.TiposDeConteudo));
    }

    [Fact]
    public async Task Bad_Request_em_SOAP_nao_e_repetido()
    {
        using var amb = new Ambiente((_, _) => (HttpStatusCode.InternalServerError,
            "<?xml version=\"1.0\"?><env:Envelope><env:Body><env:Fault><env:Reason><env:Text>Bad Request</env:Text></env:Reason></env:Fault></env:Body></env:Envelope>"));
        await Assert.ThrowsAsync<OmieException>(() => amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", []));
        Assert.Single(amb.Omie.Pedidos);
    }

    [Fact]
    public async Task Consumo_redundante_espera_o_que_o_Omie_pede_mais_3s()
    {
        var n = 0;
        using var amb = new Ambiente((_, _) => n++ == 0
            ? Ambiente.Falha(HttpStatusCode.InternalServerError, "ERROR: Consumo redundante detectado. Aguarde 47 segundos para tentar novamente (REDUNDANT).")
            : Ambiente.Ok(new JsonObject { ["ok"] = true }));

        Assert.NotNull(await amb.Cliente.ChamarAsync("financas/mf", "ListarMovimentos", []));
        Assert.Equal(2, amb.Omie.Pedidos.Count);
        Assert.Contains(TimeSpan.FromSeconds(50), amb.Esperas);
    }

    [Fact]
    public async Task Metodo_ocupado_espera_62s_uma_vez()
    {
        var n = 0;
        using var amb = new Ambiente((_, _) => n++ == 0
            ? Ambiente.Falha(HttpStatusCode.InternalServerError, "Já existe uma requisição desse método sendo executada (1)")
            : Ambiente.Ok(new JsonObject { ["ok"] = true }));

        await amb.Cliente.ChamarAsync("financas/mf", "ListarMovimentos", []);
        Assert.Equal([TimeSpan.FromSeconds(62)], amb.Esperas.Where(e => e > TimeSpan.Zero));
    }

    [Fact]
    public async Task Erro_de_parametro_nao_e_repetido()
    {
        // 10 errors on the same key + method block it for 30 minutes, so a bad parameter is sent once
        using var amb = new Ambiente((_, _) =>
            Ambiente.Falha(HttpStatusCode.InternalServerError, "ERROR: Tag [XYZ] não faz parte da estrutura do tipo complexo [clientes_list_request]! (preenchimento inválido)"));

        var erro = await Assert.ThrowsAsync<OmieException>(() => amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", new JsonObject { ["XYZ"] = 1 }));
        Assert.Single(amb.Omie.Pedidos);
        Assert.Contains("preenchimento inválido", erro.Message);
        Assert.Contains("erro:", amb.LinhasDoLog().Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "indisponível")]
    [InlineData(HttpStatusCode.BadGateway, "bad gateway")]
    [InlineData(HttpStatusCode.InternalServerError, "Tente novamente em alguns instantes")]
    public async Task Falha_passageira_e_repetida(HttpStatusCode status, string texto)
    {
        var n = 0;
        using var amb = new Ambiente((_, _) => n++ < 2 ? Ambiente.Falha(status, texto) : Ambiente.Ok(new JsonObject { ["ok"] = true }));

        Assert.NotNull(await amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", []));
        Assert.Equal(3, amb.Omie.Pedidos.Count);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], amb.Esperas.Where(e => e > TimeSpan.Zero && e < TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task Resposta_que_nao_e_JSON_e_passageira()
    {
        var n = 0;
        using var amb = new Ambiente((_, _) => n++ == 0 ? (HttpStatusCode.InternalServerError, "<html>proxy</html>") : Ambiente.Ok(new JsonObject { ["ok"] = true }));
        Assert.NotNull(await amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", []));
        Assert.Equal(2, amb.Omie.Pedidos.Count);
    }

    [Fact]
    public async Task Nao_existem_registros_vira_vazio()
    {
        using var amb = new Ambiente((_, _) =>
            Ambiente.Falha(HttpStatusCode.InternalServerError, "ERROR: Não existem registros para a página [1]!"));
        Assert.Null(await amb.Cliente.ChamarAsync("financas/mf", "ListarMovimentos", []));
        Assert.Single(amb.Omie.Pedidos);
        Assert.Contains("\"resultado\":\"vazio\"", amb.LinhasDoLog().Single());
    }

    [Fact]
    public async Task Chamada_identica_em_menos_de_60s_espera_a_janela()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok(new JsonObject { ["ok"] = true }));
        await amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", new JsonObject { ["a"] = 1, ["b"] = 2 });
        await amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", new JsonObject { ["b"] = 2, ["a"] = 1 });
        var espera = Assert.Single(amb.Esperas);
        Assert.InRange(espera, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Queda_de_rede_tenta_em_15_30_60s_e_desiste()
    {
        using var amb = new Ambiente((_, _) => throw new HttpRequestException("WinError 10054"));
        var erro = await Assert.ThrowsAsync<OmieException>(() => amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", []));
        Assert.Contains("falha de rede", erro.Message);
        Assert.Equal([TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)], amb.Esperas);
        Assert.Equal(4, amb.Omie.Pedidos.Count);
    }

    [Fact]
    public async Task Desfaz_entidades_HTML_nos_nomes()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok(new JsonObject
        {
            ["clientes_cadastro"] = new JsonArray(new JsonObject { ["razao_social"] = "B&apos;LOG &amp; CIA" }),
        }));
        var r = await amb.Cliente.ChamarAsync("geral/clientes", "ListarClientes", []);
        Assert.Equal("B'LOG & CIA", r!["clientes_cadastro"]![0]!["razao_social"]!.GetValue<string>());
    }
}
