# Instalação, build e detalhes técnicos

Como instalar o conector, gerar o pacote e o que ele faz por dentro.

## Instalar

1. Pegue o arquivo `.mcpb`.
2. No Claude Desktop (Windows), abra Settings > Extensions e arraste o arquivo para lá, ou dê dois cliques nele.
3. Preencha o nome da empresa e o APP KEY e o APP SECRET dela no Omie (Omie > Meus Aplicativos). Até duas empresas. O Claude Desktop guarda as chaves no armazenamento seguro do sistema operacional.

O servidor é um único programa do Windows com tudo de que precisa dentro.

Um pacote montado para um cliente pode levar as chaves dentro, e aí a pessoa só arrasta o arquivo. Esse arquivo passa a ser tão sensível quanto as chaves: entregue em mãos, nunca por e-mail ou grupo de conversa. Se vazar, gere chaves novas no Omie e monte o pacote de novo. Quem tirar as chaves de um pacote consegue usá-las fora deste conector, com acesso total.

## O que esperar

- O Omie responde mais ou menos uma página de 50 a 100 registros por segundo, então perguntas sobre anos de notas levam minutos. O Claude avisa que ainda está buscando e retoma de onde parou.
- Toda chamada ao Omie fica registrada em `%APPDATA%\omie-mcp-readonly\` (hora, empresa, método, filtros, número de registros, duração; sem chaves e sem os valores retornados). Logs com mais de 30 dias são apagados.
- O Omie anunciou (14/09/2026) uma mensalidade de R$ 49,90 por empresa que usa a API, ainda sem data de início.
- Os dados do Omie passam pelo Claude para gerar a resposta. No plano pessoal Pro ou Max, desligue o treinamento de modelos nas configurações de privacidade do Claude se essas conversas não devem ser usadas para treino; os planos Team e Enterprise não treinam com as conversas por padrão.

## Build

Precisa do .NET 10 SDK e do Node.js (só para rodar o empacotador oficial `mcpb`).

```powershell
dotnet test
.\build\empacotar.ps1
```

Isso gera `artifacts\omie-mcp-readonly.mcpb`: um zip com o `manifest.json` e um exe `win-x64` autocontido, de arquivo único (uns 32 MB). Um pacote de cliente com as chaves dentro usa um manifest próprio, guardado fora deste repositório: `.\build\empacotar.ps1 -Manifest <path> -Saida <file.mcpb>`, com as chaves em `mcp_config.env` em vez de `user_config`.

`.\build\catalogo-omie.ps1` refaz o catálogo de serviços do Omie a partir da documentação pública do Omie: `docs/catalogo-omie.md` e a cópia embutida no exe, `src/OmieMcpReadonly/catalogo-omie.json`. Rode quando o Omie criar serviços novos e depois gere o build de novo.

## Configuração

O servidor lê as empresas de `OMIE_EMPRESA_<n>_NOME`, `OMIE_EMPRESA_<n>_APP_KEY` e `OMIE_EMPRESA_<n>_APP_SECRET` (n de 1 a 9). `OMIE_MCP_ESPERA_S` define quanto tempo uma chamada de ferramenta espera antes de responder "ainda buscando" (padrão 100 s, abaixo do limite do próprio Claude Desktop para uma chamada de ferramenta). `OMIE_MCP_LOG_DIR` muda a pasta do log.

## Ferramentas

O usuário nunca vê estas ferramentas; o Claude escolhe pela pergunta.

| Ferramenta | O que faz |
|---|---|
| `catalogo` | Acha qual serviço do Omie responde a uma pergunta, com os parâmetros exatos e os campos dos registros (embutido, sem chamada ao Omie) |
| `consultar` | Qualquer método de leitura do Omie, devolve até 200 registros com o total real |
| `resumir` | A mesma consulta, com contagem, soma, agrupamento por um campo e top N calculados sobre todos os registros |
| `clientes_novos` | Cadastros incluídos num período com a tag Cliente, por mês, e todos os cadastros por tag |
| `produtos_mais_vendidos` | Itens das notas de pedidos de venda, quantidade e receita por produto |
| `faturamento_por_cliente` | Total das notas por cliente, com a mesma regra do relatório de faturamento do Omie |
| `contas_receber_vencidas` | Contas a receber vencidas por cliente, valor ainda em aberto, com as contas pagas em parte |
| `contas_pagar_proximos_dias` | Contas a pagar em aberto que vencem nos próximos N dias, por fornecedor e por dia |

## Detalhes técnicos

- C# em .NET 10 com o [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) oficial 2.3.0, transporte stdio.
- A trava fica em `ReadOnlyGuard.cs` e roda dentro do `OmieClient`, a única classe que fala com a rede (um teste confere isso).
- Regras do Omie trazidas de um projeto de extração anterior: uma chamada por vez para cada chave e método, esperar a janela de 60 s de "consumo redundante" e o bloqueio de 62 s de "método sendo executado", repetir só falhas passageiras (um parâmetro errado repetido 10 vezes bloqueia a chave por 30 minutos), entidades HTML decodificadas nos nomes. As requisições saem com `application/json` exato; com `; charset=utf-8` o Omie responde toda chamada com um "Bad Request" em SOAP.
- Os campos de paginação vêm do catálogo: o Omie escreve esses campos de cinco jeitos, e alguns métodos de listagem nem paginam.
- Buscas longas rodam em segundo plano e continuam depois que a chamada da ferramenta termina; os resultados ficam em cache por 15 minutos, então uma pergunta seguinte sobre os mesmos dados não chama o Omie de novo.
- Como cada receita foi conferida com as telas do Omie está em [receitas.md](receitas.md).

## Licença

PolyForm Noncommercial 1.0.0 (texto em [LICENSE](../LICENSE)). Uso comercial precisa de uma licença à parte com o autor.
