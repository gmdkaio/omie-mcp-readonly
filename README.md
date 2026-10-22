# omie-mcp-readonly

Pergunte ao Claude sobre o Omie da sua empresa em português comum e receba respostas calculadas com os dados do Omie na hora. Roda no seu próprio PC como extensão do Claude Desktop, lê qualquer parte do Omie e não consegue criar, alterar nem excluir nada lá.

## Por que só leitura

As chaves do Omie não aceitam limite: cada empresa tem uma chave com acesso total a todos os módulos, então quem guarda a chave decide o que pode acontecer com o ERP, e pedir a uma IA que não escreva não garante nada. Este conector recusa no PC toda chamada que não seja de leitura, antes de ela chegar ao Omie. Os servidores MCP do Omie feitos pela comunidade e encontrados em 22/10/2026 escrevem todos, vários excluem registros ou pagam contas, um guarda as chaves num servidor de terceiros e nenhum atende mais de uma empresa.

## Recursos

- Só leitura no código: qualquer método que não comece com `Listar`, `Pesquisar`, `Consultar` ou `Obter` é recusado, e a recusa fica no log.
- Totais, contagens, médias e rankings são calculados no código sobre todos os registros, e o Claude cita esses números.
- Cada resposta diz como o número saiu: empresa, período, o que foi contado e o que ficou de fora.
- Receitas fixas para as perguntas de sempre (clientes novos, produtos mais vendidos, faturamento por cliente e por vendedor, contas a receber vencidas, contas a pagar dos próximos dias, despesas por categoria, pedidos em aberto), comparadas com as telas do próprio Omie antes de valer.
- Um catálogo embutido de todos os serviços do Omie, com os parâmetros e campos de cada método, para as perguntas que as receitas não cobrem.
- Mais de uma empresa na mesma instalação, com respostas por empresa ou somadas.
- Consultas longas continuam rodando em segundo plano, e o Claude retoma de onde parou.
- Toda chamada ao Omie fica registrada no PC por 30 dias.

## Módulos

O que ele consegue ler, agrupado como no Omie. A lista completa, com 134 serviços e 201 métodos de leitura, está em [docs/catalogo-omie.md](docs/catalogo-omie.md).

| Módulo | O que dá para puxar |
|---|---|
| Geral | Clientes e fornecedores, produtos, categorias, departamentos, projetos, contas correntes, vendedores, contas do DRE |
| CRM | Contas, contatos, oportunidades, tarefas |
| Finanças | Contas a pagar e a receber, movimentos financeiros, extratos, caixa, resumos de finanças |
| Compras, Estoque e Produção | Pedidos de compra, notas de entrada, ordens de produção, posição e movimentos de estoque |
| Vendas e NF-e | Pedidos de venda, notas emitidas, tabelas de preço, remessas, CT-e |
| Serviços e NFS-e | Ordens de serviço, contratos, NFS-e |
| Painel do Contador | Resumo do contador e arquivos XML |

## Exemplos

> quantos clientes novos tivemos em 2026?

O Claude pergunta qual empresa e dá a contagem mês a mês. Diz que cliente novo é um cadastro incluído no período com a tag Cliente, e quantos cadastros estavam sem tag.

> quem está devendo pra gente?

O valor ainda em aberto por cliente, com as contas pagas em parte, do maior para o menor.

> cria um cliente novo chamado Teste

Recusado antes de chegar ao Omie. O Claude explica que o conector só lê e que isso tem que ser feito no Omie.
