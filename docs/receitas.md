# Recipes: definitions and checks against Omie

Each recipe is a fixed query with its definition decided once. It ships with `conferida: true` in `Receitas.cs` only after its numbers matched the Omie screen named below, for a period chosen at check time. Until then the tool answer carries `aviso_conferencia` and Claude tells the user the calculation was not checked yet.

| Recipe | Omie call | Definition | Screen compared | Real data | Checked on screen |
|---|---|---|---|---|---|
| `clientes_novos` | `geral/clientes` `ListarClientes`, `filtrar_por_data_de/ate`, `filtrar_apenas_inclusao` `S` | cadastro with `info.dInc` in the period (also filtered here) and the tag Cliente, by month; every cadastro included in the period, by tag and without tag, as detail | Clientes > Exibir todos, Inclusão "Esse ano" | 22/10/2026 | 22/10/2026: one company, the year to date; total, month by month and by tag all match |
| `produtos_mais_vendidos` | `produtos/nfconsultar` `ListarNF` with `cDetalhesPedido` `S`, `dEmiInicial/dEmiFinal`, `tpNF` `1` | items of the invoices that count as billing (below); receita = `det.prod.vProd`, quantidade = `det.prod.qCom` in the product's unit (`det.prod.uCom`) | Faturamento por Período by product (Total de Mercadoria, Quantidade), default view | 22/10/2026 | no |
| `faturamento_por_cliente` | same invoices | `total.ICMSTot.vNF` per `nfDestInt.cnpj_cpf` | Faturamento por Período, default view (Pedido de Venda, Autorizado) | 22/10/2026 | 22/10/2026: two companies, one month and a whole year; total and invoice count match to the cent (the ranking per client was not compared line by line) |
| `contas_receber_vencidas` | `financas/mf` `ListarMovimentos`, `cTpLancamento` `CR`, `cStatus` `ATRASADO`, `dDtVencDe` 01/01/2000 to today | titles due before today, `resumo.cLiquidado` not `S`, `resumo.nValAberto` > 0 (partly received ones included), per client | Finanças Resumo and Contas a Receber em Atraso | 22/10/2026 | 22/10/2026: one company; total matches to the cent |
| `contas_pagar_proximos_dias` | `financas/mf` `ListarMovimentos`, `cTpLancamento` `CP`, `cStatus` `EMABERTO`, `dDtPrevDe/Ate` today to today + N - 1 | `resumo.nValAberto` per supplier and per day | Contas a Pagar, em aberto, same window | 22/10/2026 | 22/10/2026: one company, 30 days; totals per date and per creditor match (two bills paid in Omie during the check explain the rest) |
Invoices that count as billing (both invoice recipes) follow Omie's "Faturamento por Período" report in the view it opens with, Operação = Pedido de Venda and Situação = Autorizado: `ListarNF` with `cDetalhesPedido` `S`, `ide.tpNF` = 1, `ide.finNFe` ≠ 4, `ide.dCan` and `ide.dInut` empty, `ide.cDeneg` ≠ `S`, `pedido.opPedido` 11 or 12 (sale of product), and the order not cancelled, returned or partly returned (`pedido.cCancelado`, `cDevolvido`, `cDevParcial` ≠ `S`). Remessas (`opPedido` 14: samples, moulds, conserto, entrega futura) stay out. Customer returns are not subtracted, as in that view.

## What the first run on real data showed (22/10/2026)

Every recipe ran for two companies at once; the longest took 35 s (the client register, about 47 pages per company, read alongside the titles).

- Omie refuses `Content-Type: application/json; charset=utf-8` with a SOAP "Bad Request" envelope on every method. The client sends exactly `application/json`, and that envelope is no longer retried.
- All the field paths above exist in real responses. Titles also carry the client's CNPJ (`detalhes.cCPFCNPJCliente`) and invoice items carry the CFOP (`det.prod.CFOP`).
- `filtrar_por_data_de/ate` with `filtrar_apenas_inclusao` does filter by `info.dInc`: every record returned was inside the period.
- `cStatus` `ATRASADO` on `ListarMovimentos` also returns some titles with status RECEBIDO that still have an open amount. The first version filtered `detalhes.cStatus` again and left them out; the screen check showed that was wrong (see below).
- A cancelled invoice comes with `ide.dCan` filled and `ide.cDeneg` `N`. The `dCan` filter is the one that matters.
- `qCom` is in each product's commercial unit, often `MI` (milheiro). A quantity ranking compares products in different units, so every product now carries its unit and the overall quantity sum is not to be quoted.
- Government payees (DAS, payroll, taxes) share the CNPJ `00.000.000/0000-00`. A CNPJ made only of zeros no longer serves as the grouping key, so they don't merge under one name.
- Many cadastros have no tag at all, and the register also holds suppliers, carriers and staff. Decided on 22/10/2026: a new client is a cadastro included in the period with the tag Cliente (with or without other tags). The rest is reported under `todos_os_cadastros`.

## Second check: Omie screens and a management DRE (22/10/2026)

- Overdue receivables: `cStatus` `ATRASADO` misses titles received in part, which the API returns as RECEBIDO with `cLiquidado` `N` and the rest still open; Omie's "Contas a Receber em Atraso" screen counts them. The recipe now selects on the open balance and matched the screen's "a receber hoje" total to the cent. That screen's overdue list can leave out a title that fell due yesterday.
- Billing: counting every issued invoice made billing up to 8% higher than a management DRE in a month (moulds sent to clients, conserto returns and remessas). Leaving out CFOP x.9xx came within 0,5% but also dropped invoices Omie counts as sales (a large CFOP 6.909 sale). Omie's report in its default view (Pedido de Venda, Autorizado) matched the DRE to the cent, so the recipes now use that rule; one month and a whole year matched it to the cent and to the invoice for both companies. An invoice Omie shows as "Rejeitado" has no flag in the API; the one found was a remessa, which this rule leaves out.
- Matched exactly on screen: a bank account balance at month end (realizado and previsto), a product's stock, the count of open sales orders by stage.

## To settle at the screen check
- Whether `EMABERTO` on contas a pagar includes titles already overdue (the recipe leaves out anything with previsão before today either way).

## How to record a check

1. Pick a closed period (a month works) and one company.
2. Ask the question in Claude Desktop with the extension installed, and open the matching Omie screen with the same filters.
3. Compare the total and the first five lines of the ranking.
4. When they match, set `conferida: true` for that recipe in `Receitas.cs` and write the date and period in the Screen column above.
