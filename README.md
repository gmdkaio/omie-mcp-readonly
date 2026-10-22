# omie-mcp-readonly

Ask Claude about your company's Omie ERP in plain Portuguese and get answers computed from live Omie data. It runs on your own PC as a Claude Desktop extension, reads any part of Omie, and can't create, change or delete anything there.

## Why read-only

Omie keys can't be limited: each company has one key with full access to every module, so whatever holds the key decides what can happen to the ERP, and asking an AI model not to write is no guarantee. This connector refuses every call that isn't a read on your PC, before it reaches Omie. The community Omie MCP servers found on 22/10/2026 can all write, several can delete records or pay bills, one keeps the keys on a third-party server, and none handles more than one company.

## Features

- Read-only in code: any method that doesn't start with `Listar`, `Pesquisar`, `Consultar` or `Obter` is refused, and the refusal is logged.
- Totals, counts and rankings are computed in code over every record, and Claude quotes them.
- Each answer says how the number was obtained: company, period, what was counted and what was left out.
- Fixed recipes for the usual questions (new clients, best-selling products, billing per client, overdue receivables, payables due soon), each one compared with Omie's own screens before it counts as done.
- A built-in catalog of every Omie service, with each method's parameters and fields, for the questions the recipes don't cover.
- More than one company in the same install, with answers per company or combined.
- Long queries keep running in the background, and Claude picks up where it stopped.
- Every call to Omie is logged on the PC for 30 days.

## Modules

What it can read, grouped as in Omie. The full list of 134 services and 201 read methods is in [docs/catalogo-omie.md](docs/catalogo-omie.md).

| Module | What it can pull |
|---|---|
| Geral | Clients and suppliers, products, categories, departments, projects, bank accounts, sellers, DRE accounts |
| CRM | Accounts, contacts, opportunities, tasks |
| Finanças | Contas a pagar and a receber, financial movements, bank statements, cash, finance summaries |
| Compras, Estoque e Produção | Purchase orders, incoming invoices, production orders, stock position and movements |
| Vendas e NF-e | Sales orders, issued invoices, price tables, shipments, CT-e |
| Serviços e NFS-e | Service orders, contracts, NFS-e |
| Painel do Contador | Accountant summary and XML files |

## Examples

> quantos clientes novos tivemos em 2026?

Claude asks which company, then gives the count month by month. It says a new client is a cadastro included in the period with the tag Cliente, and how many cadastros had no tag.

> quem está devendo pra gente?

The amount still open per client, bills paid in part included, largest first.

> cria um cliente novo chamado Teste

Refused before anything reaches Omie. Claude explains that the connector only reads and that this has to be done in Omie.
