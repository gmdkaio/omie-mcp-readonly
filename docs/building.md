# Building and running

## Install

1. Get the `.mcpb` file.
2. In Claude Desktop (Windows), open Settings > Extensions and drag the file in, or double-click it.
3. Fill in the company name and its Omie APP KEY and APP SECRET (Omie > Meus Aplicativos). Up to two companies. Claude Desktop keeps the keys in the operating system's secure storage.

The server is a single Windows program with everything it needs inside.

A bundle prepared for one client can carry the keys inside, so the person only drags the file in. That file is then as sensitive as the keys: hand it over directly, never by e-mail or group chat. If it leaks, generate new keys in Omie and rebuild the bundle. Anyone who gets the keys out of a bundle can use them outside this connector with full access.

## What to expect

- Omie answers about one page of 50 to 100 records per second, so questions over years of invoices take minutes. Claude says it is still fetching and picks up where it stopped.
- Every call to Omie is logged in `%APPDATA%\omie-mcp-readonly\` (time, company, method, filters, number of records, duration; no keys, no amounts returned). Logs older than 30 days are deleted.
- Omie announced (14/09/2026) a monthly fee of R$ 49,90 per company using its API, with no start date yet.
- Omie data passes through Claude to answer. On a personal Pro or Max plan, turn off model training in Claude's privacy settings if these chats shouldn't be used for training; Team and Enterprise plans don't train on chats by default.

## Build

Needs the .NET 10 SDK and Node.js (only to run the official `mcpb` packer).

```powershell
dotnet test
.\build\empacotar.ps1
```

That writes `artifacts\omie-mcp-readonly.mcpb`: a zip with `manifest.json` and a self-contained `win-x64` single-file exe (about 32 MB). A client bundle with keys built in uses its own manifest, kept outside this repo: `.\build\empacotar.ps1 -Manifest <path> -Saida <file.mcpb>`, with the keys in `mcp_config.env` instead of `user_config`.

`.\build\catalogo-omie.ps1` rebuilds the catalog of Omie services from Omie's public docs: `docs/catalogo-omie.md` and the copy built into the exe, `src/OmieMcpReadonly/catalogo-omie.json`. Run it when Omie adds services, then rebuild.

## Settings

The server reads companies from `OMIE_EMPRESA_<n>_NOME`, `OMIE_EMPRESA_<n>_APP_KEY` and `OMIE_EMPRESA_<n>_APP_SECRET` (n from 1 to 9). `OMIE_MCP_ESPERA_S` sets how long a tool call waits before answering "still fetching" (default 100 s, under Claude Desktop's own limit on a tool call). `OMIE_MCP_LOG_DIR` moves the log.

## Tools

The user never sees these; Claude picks them from the question.

| Tool | What it does |
|---|---|
| `catalogo` | Finds which Omie service answers a question, with its exact parameters and record fields (built in, no call to Omie) |
| `consultar` | Any read-only Omie method, up to 200 records back with the real total |
| `resumir` | Same query, with count, sum, group by one field and top N computed over every record |
| `clientes_novos` | Cadastros included in a period with the tag Cliente, by month, and every cadastro by tag |
| `produtos_mais_vendidos` | Items of invoices from sales orders, quantity and revenue per product |
| `faturamento_por_cliente` | Invoice totals per client, the same rule as Omie's billing report |
| `contas_receber_vencidas` | Overdue receivables per client, amount still open, including bills paid in part |
| `contas_pagar_proximos_dias` | Open payables due in the next N days, per supplier and per day |

## Technical details

- C# on .NET 10 with the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) 2.3.0, stdio transport.
- The guard is in `ReadOnlyGuard.cs` and runs in `OmieClient`, the only class that talks to the network (a test checks that).
- Omie rules carried over from an earlier extraction project: one call at a time per key and method, waiting out the 60 s "consumo redundante" window and the 62 s "método sendo executado" lock, retrying only transient faults (a bad parameter retried 10 times blocks the key for 30 minutes), HTML entities unescaped in names. Requests go out as exactly `application/json`; with `; charset=utf-8` Omie answers every call with a SOAP "Bad Request".
- Paging fields come from the catalog: Omie spells them five ways, and some list methods don't page at all.
- Long fetches run in the background and outlive the tool call; results stay cached for 15 minutes, so a follow-up question on the same data doesn't call Omie again.
- How each recipe was checked against Omie's screens is in [receitas.md](receitas.md).

## License

Not chosen yet.
