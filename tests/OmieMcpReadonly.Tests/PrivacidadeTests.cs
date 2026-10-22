using System.Text.Json.Nodes;

namespace OmieMcpReadonly.Tests;

public class PrivacidadeTests
{
    [Fact]
    public void Remove_contato_endereco_e_banco_e_mantem_o_negocio()
    {
        var cliente = new JsonObject
        {
            ["razao_social"] = "Maria da Silva",
            ["cnpj_cpf"] = "123.456.789-01",
            ["email"] = "maria@exemplo.com",
            ["telefone1_ddd"] = "41",
            ["telefone1_numero"] = "99999-0000",
            ["endereco"] = "Rua A",
            ["endereco_numero"] = "10",
            ["cep"] = "80000-000",
            ["bairro"] = "Centro",
            ["cidade"] = "CURITIBA (PR)",
            ["estado"] = "PR",
            ["dadosBancarios"] = new JsonObject { ["agencia"] = "1", ["conta_corrente"] = "2" },
            ["contato"] = "Maria",
            ["info"] = new JsonObject { ["dInc"] = "01/01/2026" },
            ["fax_numero"] = "",
        };
        Privacidade.Limpar(cliente);

        Assert.Equal("***.456.789-**", cliente["cnpj_cpf"]!.GetValue<string>());
        foreach (var campo in new[] { "email", "telefone1_ddd", "telefone1_numero", "endereco", "endereco_numero", "cep", "bairro", "dadosBancarios" })
            Assert.Equal(Privacidade.Removido, cliente[campo]!.GetValue<string>());
        Assert.Equal("Maria da Silva", cliente["razao_social"]!.GetValue<string>());
        Assert.Equal("CURITIBA (PR)", cliente["cidade"]!.GetValue<string>());
        Assert.Equal("01/01/2026", cliente["info"]!["dInc"]!.GetValue<string>());
        // an empty field stays empty: "no fax on file" is still visible
        Assert.Equal("", cliente["fax_numero"]!.GetValue<string>());
    }

    [Fact]
    public void Cnpj_de_empresa_fica_inteiro_e_cpf_em_listas_aninhadas_e_mascarado()
    {
        var resposta = new JsonObject
        {
            ["movimentos"] = new JsonArray(
                new JsonObject { ["detalhes"] = new JsonObject { ["cCPFCNPJCliente"] = "11.444.777/0001-61" } },
                new JsonObject { ["detalhes"] = new JsonObject { ["cCPFCNPJCliente"] = "98765432100" } }),
        };
        Privacidade.Limpar(resposta);
        Assert.Equal("11.444.777/0001-61", resposta["movimentos"]![0]!["detalhes"]!["cCPFCNPJCliente"]!.GetValue<string>());
        Assert.Equal("***.654.321-**", resposta["movimentos"]![1]!["detalhes"]!["cCPFCNPJCliente"]!.GetValue<string>());
    }

    [Fact]
    public async Task Consultar_nunca_devolve_email_nem_cpf_inteiro()
    {
        using var amb = new Ambiente((_, _) => Ambiente.Ok(new JsonObject
        {
            ["pagina"] = 1, ["total_de_paginas"] = 1, ["total_de_registros"] = 1,
            ["clientes_cadastro"] = new JsonArray(new JsonObject { ["razao_social"] = "Joao", ["cnpj_cpf"] = "111.222.333-44", ["email"] = "joao@x.com" }),
        }));
        var texto = await Ferramentas.Consultar(amb.Servico, "MATRIZ", "geral/clientes", "ListarClientes", null);
        Assert.DoesNotContain("joao@x.com", texto);
        Assert.DoesNotContain("111.222.333-44", texto);
        Assert.Contains("***.222.333-**", texto);
    }
}
