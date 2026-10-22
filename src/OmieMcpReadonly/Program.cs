using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using OmieMcpReadonly;

var versao = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
var empresas = Empresas.DoAmbiente();
var log = new CallLog();
log.ApagarAntigos(DateTime.Now);

// one HttpClient for every company; the per-key rules live in each OmieClient
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd($"omie-mcp-readonly/{versao}");
var clientes = empresas.Lista.ToDictionary(e => e.Nome, e => new OmieClient(e, http, log));
var catalogo = Catalogo.Embutido();
var servico = new Servico(empresas, new Buscador(clientes, catalogo), Servico.EsperaDoAmbiente());

var builder = Host.CreateApplicationBuilder(args);
// stdout is the MCP channel: every log line goes to stderr, which Claude Desktop keeps in its MCP logs
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(servico);
builder.Services.AddSingleton(catalogo);
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new Implementation { Name = "omie-mcp-readonly", Version = versao };
        o.ServerInstructions = Instrucoes.Texto(empresas);
    })
    .WithStdioServerTransport()
    .WithTools<Ferramentas>()
    .WithTools<Receitas>()
    // a refused write, a missing company or an Omie fault goes back to Claude as the tool's error text, without the
    // SDK's "An error occurred invoking" prefix and without a stack trace in Claude Desktop's log
    .WithRequestFilters(f => f.AddCallToolFilter(proximo => async (pedido, ct) =>
    {
        try
        {
            return await proximo(pedido, ct);
        }
        catch (ModelContextProtocol.McpException e)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = e.Message }] };
        }
    }));

await builder.Build().RunAsync();
