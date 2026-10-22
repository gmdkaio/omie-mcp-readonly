# omie-mcp-readonly

Ask Claude about your company's Omie ERP in plain Portuguese ("quantos clientes novos tivemos em 2026?", "quais produtos mais vendemos?") and get answers computed from live Omie data. It runs on your own PC as a Claude Desktop extension and can only read: it cannot create, change or delete anything in Omie, and that is enforced in code.

## Why read-only, and why another Omie connector

Omie API keys can't be restricted. Each Omie company has one app_key and app_secret with full access to every module: there is no read-only key, no per-module scope, and the only way to rotate is "Gerar nova chave", which breaks every integration using the old key ([Omie help](https://ajuda.omie.com.br/pt-BR/articles/499061-obtendo-a-chave-de-acesso-para-integracoes-de-api)). So whatever holds the keys decides what can happen to the ERP, and for someone talking to an AI model, "please don't write" in a prompt is not a guarantee.

This connector refuses, on your PC, every call whose method doesn't start with `Listar`, `Pesquisar`, `Consultar` or `Obter`. A refused call never reaches Omie and shows up in the local log as refused.

The community Omie MCP servers found on 22/10/2026 can all write; several can delete records or pay bills, one exposes a "call any endpoint" tool, one is hosted with the keys on a third party, and none handles more than one Omie company. There is no official Omie or Anthropic connector.

What it does not protect against: the keys still exist. Anyone who gets a bundle file with keys inside, or reads them from the extension settings, can use them outside this connector with full access. Treat the bundle like the keys themselves.

## Status

Version 0.1, not released. The read-only guard, paging, Omie's retry rules, the summaries and the local log are built and tested (67 tests against a fake Omie). Every tool has also run against a real Omie account with two companies. The five recipes below still need to match the matching Omie screen before they count as done ([docs/receitas.md](docs/receitas.md)); until then Claude says so in its answers.

## How it is used

### Install

1. Get the `.mcpb` file.
2. In Claude Desktop (Windows), open Settings > Extensions and drag the file in, or double-click it.
3. Fill in the company name and its Omie APP KEY and APP SECRET (Omie > Meus Aplicativos). Up to two companies. The keys are stored by Claude Desktop in the operating system's secure storage.

Nothing else to install: the server is a single Windows program with everything it needs inside.

A bundle prepared for a specific company can come with the keys already inside, so the person only drags the file in. That file is then as sensitive as the keys: hand it over directly, never by email or group chat. If it leaks, generate new keys in Omie and rebuild the bundle.

### Ask questions

Type questions as you would to a colleague who has Omie open:

- quantos clientes novos tivemos em 2026?
- quais produtos mais vendemos no primeiro trimestre, em quantidade e preço?
- quanto faturamos com cada cliente em setembro?
- quem está devendo para a gente?
- o que temos para pagar nos próximos 30 dias?

With more than one company, Claude asks which one if the question doesn't say ("as duas" combines them).

### What to expect

- The connector computes totals, counts and rankings in code over every record, and Claude quotes those numbers.
- Each answer says how the number was obtained: the company, the period, what was counted and what was left out (returns, cancelled invoices).
- Omie answers about one page of 50 to 100 records per second, so questions over years of invoices take minutes. Claude says it is still fetching and picks up where it stopped.
- Ask it to create or change anything and it explains that it can only read; do that in Omie.
- Every call to Omie is logged in `%APPDATA%\omie-mcp-readonly\` (time, company, method, filters, number of records, duration; no keys, no amounts returned). Logs older than 30 days are deleted.
- Omie announced (14/09/2026) a monthly fee per company using its API, R$ 49,90, with no start date yet. The fee is Omie's.
- Your Omie data passes through Claude to answer. On a personal Pro or Max plan, turn off model training in Claude's privacy settings if you don't want these chats used for training; Team and Enterprise plans don't train on chats by default.

## What Claude can call

The user never sees these; Claude picks them from the question.

| Tool | What it does |
|---|---|
| `consultar` | Any read-only Omie method, up to 200 records back with the real total (it stops paging at 200; totals go through `resumir`) |
| `resumir` | Same query, with count, sum, group by one field and top N computed over every record |
| `clientes_novos` | Cadastros included in a period, by month and by tag |
| `produtos_mais_vendidos` | Items of issued invoices (no returns, no cancelled), quantity and revenue per product |
| `faturamento_por_cliente` | Invoice totals per client |
| `contas_receber_vencidas` | Overdue receivables per client, amount still open |
| `contas_pagar_proximos_dias` | Open payables due in the next N days, per supplier and per day |

## Building

Needs the .NET 10 SDK and Node.js (only to run the official `mcpb` packer).

```powershell
dotnet test
.\build\empacotar.ps1
```

That writes `artifacts\omie-mcp-readonly.mcpb`: a zip with `manifest.json` and a self-contained `win-x64` single-file exe (about 32 MB). A client bundle with keys built in uses its own manifest, kept outside this repo: `.\build\empacotar.ps1 -Manifest <path> -Saida <file.mcpb>`, with the keys in `mcp_config.env` instead of `user_config`.

The server reads companies from `OMIE_EMPRESA_<n>_NOME`, `OMIE_EMPRESA_<n>_APP_KEY` and `OMIE_EMPRESA_<n>_APP_SECRET` (n from 1 to 9). `OMIE_MCP_ESPERA_S` sets how long a tool call waits before answering "still fetching" (default 100 s, under Claude Desktop's own limit on a tool call). `OMIE_MCP_LOG_DIR` moves the log.

### Technical details

- C# on .NET 10 with the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) 2.3.0, stdio transport.
- The guard is in `ReadOnlyGuard.cs` and runs in `OmieClient`, the only class that talks to the network (a test checks that).
- Omie rules carried over from a previous extraction project: one call at a time per key and method, waiting out the 60 s "consumo redundante" window and the 62 s "método sendo executado" lock, retrying only transient faults (a bad parameter retried 10 times blocks the key for 30 minutes), HTML entities unescaped in names.
- Long fetches run in the background and outlive the tool call; results stay cached for 15 minutes, so a follow-up question on the same data doesn't call Omie again.

## License

Not chosen yet.
