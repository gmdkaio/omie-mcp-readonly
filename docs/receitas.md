# Receitas: definições e conferência com o Omie

Cada receita é uma consulta fixa com a definição decidida uma vez só. Ela sai com `conferida: true` em `Receitas.cs` só depois que os números bateram com a tela do Omie indicada abaixo, num período escolhido na hora da conferência. Até lá a resposta da ferramenta traz `aviso_conferencia` e o Claude avisa o usuário que o cálculo ainda não foi conferido.

| Receita | Chamada no Omie | Definição | Tela comparada | Dados reais | Conferida na tela |
|---|---|---|---|---|---|
| `clientes_novos` | `geral/clientes` `ListarClientes`, `filtrar_por_data_de/ate`, `filtrar_apenas_inclusao` `S` | cadastro com `info.dInc` no período (filtrado aqui também) e a tag Cliente, por mês; todos os cadastros incluídos no período, por tag e sem tag, como detalhe | Clientes > Exibir todos, Inclusão "Esse ano" | 22/10/2026 | 22/10/2026: uma empresa, o ano até a data; total, mês a mês e por tag batem |
| `produtos_mais_vendidos` | `produtos/nfconsultar` `ListarNF` com `cDetalhesPedido` `S`, `dEmiInicial/dEmiFinal`, `tpNF` `1` | itens das notas que contam como faturamento (abaixo); receita = `det.prod.vProd`, quantidade = `det.prod.qCom` na unidade do produto (`det.prod.uCom`) | Faturamento por Período por produto (Total de Mercadoria, Quantidade), visão padrão | 22/10/2026 | 22/10/2026: duas empresas, um mês; total, quantidade, número de produtos e os 20 primeiros por valor e por quantidade batem ao centavo |
| `faturamento_por_cliente` | as mesmas notas | `total.ICMSTot.vNF` por `nfDestInt.cnpj_cpf` | Faturamento por Período, visão padrão (Pedido de Venda, Autorizado) | 22/10/2026 | 22/10/2026: duas empresas, um mês e um ano inteiro; total e número de notas batem no centavo (o ranking por cliente não foi comparado linha a linha) |
| `contas_receber_vencidas` | `financas/mf` `ListarMovimentos`, `cTpLancamento` `CR`, `cStatus` `ATRASADO`, `dDtVencDe` 01/01/2000 até hoje | títulos vencidos antes de hoje, `resumo.cLiquidado` diferente de `S`, `resumo.nValAberto` > 0 (com os recebidos em parte), por cliente | Finanças Resumo e Contas a Receber em Atraso | 22/10/2026 | 22/10/2026: uma empresa; total bate no centavo |
| `contas_pagar_proximos_dias` | `financas/mf` `ListarMovimentos`, `cTpLancamento` `CP`, `cStatus` `EMABERTO`, `dDtPrevDe/Ate` de hoje até hoje + N - 1 | `resumo.nValAberto` por fornecedor e por dia | Contas a Pagar, em aberto, mesma janela | 22/10/2026 | 22/10/2026: uma empresa, 30 dias; totais por data e por credor batem (duas contas pagas no Omie durante a conferência explicam o resto) |

As notas que contam como faturamento (nas duas receitas de notas) seguem o relatório "Faturamento por Período" do Omie na visão com que ele abre, Operação = Pedido de Venda e Situação = Autorizado: `ListarNF` com `cDetalhesPedido` `S`, `ide.tpNF` = 1, `ide.finNFe` ≠ 4, `ide.dCan` e `ide.dInut` vazios, `ide.cDeneg` ≠ `S`, `pedido.opPedido` 11 ou 12 (venda de produto), e o pedido não cancelado, devolvido ou devolvido em parte (`pedido.cCancelado`, `cDevolvido`, `cDevParcial` ≠ `S`). Remessas (`opPedido` 14: amostras, moldes, conserto, entrega futura) ficam de fora. O número principal não desconta devoluções de clientes, como nessa visão. Elas vêm à parte em `devolucoes` (`ListarNF` com `tpNF` 0 e `ide.finNFe` 4, notas de entrada de devolução emitidas no período, quase sempre CFOP 1.201 ou 2.201), e o valor já descontado sai em `faturamento_menos_devolucoes` e `receita_menos_devolucoes`.

## O que a primeira rodada com dados reais mostrou (22/10/2026)

Todas as receitas rodaram para duas empresas ao mesmo tempo; a mais longa levou 35 s (o cadastro de clientes, umas 47 páginas por empresa, lido junto com os títulos).

- O Omie recusa `Content-Type: application/json; charset=utf-8` com um envelope SOAP de "Bad Request" em todos os métodos. O cliente manda `application/json` exato, e esse envelope não é mais repetido.
- Todos os caminhos de campo acima existem nas respostas reais. Os títulos também trazem o CNPJ do cliente (`detalhes.cCPFCNPJCliente`) e os itens de nota trazem o CFOP (`det.prod.CFOP`).
- `filtrar_por_data_de/ate` com `filtrar_apenas_inclusao` filtra mesmo por `info.dInc`: todos os registros devolvidos estavam dentro do período.
- `cStatus` `ATRASADO` no `ListarMovimentos` também devolve alguns títulos com status RECEBIDO que ainda têm valor em aberto. A primeira versão filtrava `detalhes.cStatus` de novo e deixava esses de fora; a conferência na tela mostrou que isso estava errado (ver abaixo).
- Uma nota cancelada vem com `ide.dCan` preenchido e `ide.cDeneg` `N`. O filtro que vale é o de `dCan`.
- `qCom` vem na unidade comercial de cada produto, muitas vezes `MI` (milheiro). Um ranking por quantidade compara produtos em unidades diferentes, então cada produto agora leva a sua unidade e a soma geral de quantidade não deve ser citada.
- Favorecidos do governo (DAS, folha, impostos) compartilham o CNPJ `00.000.000/0000-00`. Um CNPJ só de zeros não serve mais como chave de agrupamento, para eles não se juntarem sob um nome só.
- Muitos cadastros não têm tag nenhuma, e o cadastro também guarda fornecedores, transportadoras e funcionários. Decidido em 22/10/2026: cliente novo é um cadastro incluído no período com a tag Cliente (com ou sem outras tags). O resto aparece em `todos_os_cadastros`.

## Segunda conferência: telas do Omie e um DRE gerencial (22/10/2026)

- Contas a receber vencidas: `cStatus` `ATRASADO` perde os títulos recebidos em parte, que a API devolve como RECEBIDO com `cLiquidado` `N` e o resto ainda em aberto; a tela "Contas a Receber em Atraso" do Omie conta esses títulos. A receita agora seleciona pelo saldo em aberto e bateu no centavo com o total "a receber hoje" da tela. A lista de atrasados dessa tela pode deixar de fora um título que venceu ontem.
- Faturamento: contar todas as notas emitidas deixou o faturamento até 8% acima de um DRE gerencial num mês (moldes enviados a clientes, retornos de conserto e remessas). Tirar os CFOP x.9xx chegou a 0,5% de diferença, mas também derrubou notas que o Omie conta como venda (uma venda grande com CFOP 6.909). O relatório do Omie na visão padrão (Pedido de Venda, Autorizado) bateu no centavo com o DRE, então as receitas agora usam essa regra; um mês e um ano inteiro bateram no centavo e na nota para as duas empresas. Uma nota que o Omie mostra como "Rejeitado" não tem marcação na API; a encontrada era uma remessa, que essa regra deixa de fora.
- Bateram exatamente na tela: o saldo de uma conta corrente no fim do mês (realizado e previsto), o estoque de um produto, a contagem de pedidos de venda em aberto por etapa.

## Para resolver na conferência de tela

- Se `EMABERTO` em contas a pagar inclui títulos já vencidos (a receita deixa de fora qualquer coisa com previsão antes de hoje de qualquer jeito).

## Como registrar uma conferência

1. Escolha um período fechado (um mês serve) e uma empresa.
2. Faça a pergunta no Claude Desktop com a extensão instalada e abra a tela correspondente do Omie com os mesmos filtros.
3. Compare o total e as cinco primeiras linhas do ranking.
4. Quando baterem, coloque `conferida: true` para essa receita em `Receitas.cs` e escreva a data e o período na coluna `Conferida na tela` acima.
