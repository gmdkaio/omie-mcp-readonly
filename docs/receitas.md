# Recipes: definitions and checks against Omie

Each recipe is a fixed query with its definition decided once. It ships with `conferida: true` in `Receitas.cs` only after its numbers matched the Omie screen named below, for a period chosen at check time. Until then the tool answer carries `aviso_conferencia` and Claude tells the user the calculation was not checked yet.

| Recipe | Omie call | Definition | Screen to compare | Real data | Screen |
|---|---|---|---|---|---|
| `clientes_novos` | `geral/clientes` `ListarClientes`, `filtrar_por_data_de/ate`, `filtrar_apenas_inclusao` `S` | cadastro with `info.dInc` in the period (also filtered here), by month, by tag, and how many have no tag | Clientes e Fornecedores, filtered by inclusion date | 22/10/2026 | no |
| `produtos_mais_vendidos` | `produtos/nfconsultar` `ListarNF`, `dEmiInicial/dEmiFinal`, `tpNF` `1` | one row per item (`det`), receita = `det.prod.vProd`, quantidade = `det.prod.qCom` in the product's unit (`det.prod.uCom`) | NF-e emitidas / Vendas por produto, same period | 22/10/2026 | no |
| `faturamento_por_cliente` | same invoices | `total.ICMSTot.vNF` per `nfDestInt.cnpj_cpf` | Faturamento por cliente, same period | 22/10/2026 | no |
| `contas_receber_vencidas` | `financas/mf` `ListarMovimentos`, `cTpLancamento` `CR`, `cStatus` `ATRASADO`, `dDtVencDe` 01/01/2000 to today | `resumo.nValAberto` per client (names from `ListarClientes`) | Contas a Receber, filter Atrasados | 22/10/2026 | no |
| `contas_pagar_proximos_dias` | `financas/mf` `ListarMovimentos`, `cTpLancamento` `CP`, `cStatus` `EMABERTO`, `dDtPrevDe/Ate` today to today + N - 1 | `resumo.nValAberto` per supplier and per day | Contas a Pagar, em aberto, same window | 22/10/2026 | no |

Invoices that count as billing (both invoice recipes): `ide.tpNF` = 1, `ide.finNFe` ≠ 4, `ide.dCan` empty, `ide.dInut` empty, `ide.cDeneg` ≠ `S`.

## What the first run on real data showed (22/10/2026)

Every recipe ran for two companies at once; the longest took 35 s (the client register, about 47 pages per company, read alongside the titles).

- Omie refuses `Content-Type: application/json; charset=utf-8` with a SOAP "Bad Request" envelope on every method. The client sends exactly `application/json`, and that envelope is no longer retried.
- All the field paths above exist in real responses. Titles also carry the client's CNPJ (`detalhes.cCPFCNPJCliente`) and invoice items carry the CFOP (`det.prod.CFOP`).
- `filtrar_por_data_de/ate` with `filtrar_apenas_inclusao` does filter by `info.dInc`: every record returned was inside the period.
- `cStatus` `ATRASADO` on `ListarMovimentos` also returns some titles with status RECEBIDO that still have an open amount. The recipe filters `detalhes.cStatus` again, so they stay out.
- A cancelled invoice comes with `ide.dCan` filled and `ide.cDeneg` `N`. The `dCan` filter is the one that matters.
- `qCom` is in each product's commercial unit, often `MI` (milheiro). A quantity ranking compares products in different units, so every product now carries its unit and the overall quantity sum is not to be quoted.
- Government payees (DAS, payroll, taxes) share the CNPJ `00.000.000/0000-00`. A CNPJ made only of zeros no longer serves as the grouping key, so they don't merge under one name.
- Many cadastros have no tag at all, so `por_tag` alone undercounts. The recipe now reports `sem_tag`.

## To settle at the screen check

- "Cliente novo": every cadastro included in the period, only those tagged Cliente, or those tagged Cliente plus the untagged ones.
- Whether free samples (CFOP x.911) and other non-sale CFOPs should be left out of billing. In the month checked they were well under 1% of the value.
- Whether `EMABERTO` on contas a pagar includes titles already overdue (the recipe leaves out anything with previsão before today either way).

## How to record a check

1. Pick a closed period (a month works) and one company.
2. Ask the question in Claude Desktop with the extension installed, and open the matching Omie screen with the same filters.
3. Compare the total and the first five lines of the ranking.
4. When they match, set `conferida: true` for that recipe in `Receitas.cs` and write the date and period in the Screen column above.
