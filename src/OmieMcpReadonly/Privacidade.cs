using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OmieMcpReadonly;

/// <summary>
/// Strips personal data no business question needs before Omie's answer reaches Claude: e-mails, phones, street
/// addresses and bank details are replaced by "[removido]", and a person's CPF keeps only enough digits to tell people
/// apart (***.456.789-**). Company CNPJs, names, cities and amounts stay. It runs on every page as it arrives, so
/// consultar, resumir and the recipes all see the same cleaned data.
/// </summary>
public static partial class Privacidade
{
    public const string Removido = "[removido]";

    // field names as Omie spells them across services: email, cEmail, telefone1_numero, cTelefone, celular, fax_ddd,
    // endereco, cEndereco, endereco_numero, complemento, cep, bairro, dadosBancarios, chave_pix...
    [GeneratedRegex(@"(?i)e-?mail|telefone|celular|^c?fax|whats|^c?(endereco|logradouro|complemento|cep|bairro)$|^endereco_numero$|dadosbancarios|chave_?pix")]
    private static partial Regex CampoPessoal();

    [GeneratedRegex("(?i)cpf")]
    private static partial Regex CampoCpf();

    public static JsonNode? Limpar(JsonNode? no)
    {
        switch (no)
        {
            case JsonObject obj:
                foreach (var chave in obj.Select(p => p.Key).ToList())
                {
                    var valor = obj[chave];
                    if (CampoPessoal().IsMatch(chave))
                        obj[chave] = Vazio(valor) ? valor?.DeepClone() : Removido;
                    else if (CampoCpf().IsMatch(chave) && valor is JsonValue v && v.TryGetValue<string>(out var doc))
                        obj[chave] = MascararCpf(doc);
                    else
                        Limpar(valor);
                }
                return obj;
            case JsonArray arr:
                foreach (var item in arr) Limpar(item);
                return arr;
            default:
                return no;
        }
    }

    /// <summary>A CPF (11 digits) becomes ***.456.789-**; a CNPJ (14 digits, a company) stays as it is.</summary>
    public static string MascararCpf(string documento)
    {
        var digitos = new string(documento.Where(char.IsAsciiDigit).ToArray());
        return digitos.Length == 11 ? $"***.{digitos[3..6]}.{digitos[6..9]}-**" : documento;
    }

    // an empty field stays empty, so "no e-mail on file" is still visible as such
    private static bool Vazio(JsonNode? valor) =>
        valor is null || (valor is JsonValue v && v.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s));
}
