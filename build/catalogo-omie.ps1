# Rebuilds docs/catalogo-omie.md: every Omie API service, from Omie's public docs (no keys needed), with the methods
# the connector can call and the ones it refuses. Run it when Omie adds services.
#   .\build\catalogo-omie.ps1
param([string]$Saida = (Join-Path $PSScriptRoot "..\docs\catalogo-omie.md"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Web
function Texto($html) { ([Web.HttpUtility]::HtmlDecode(($html -replace '<[^>]+>', ' ')) -replace '\s+', ' ').Trim() }

# same rule as ReadOnlyGuard.cs
$leitura = '^(Listar|Pesquisar|Consultar|Obter)[A-Z][A-Za-z0-9]*$'
# names that read but fall outside that rule: shown apart so the allowlist can be reviewed
$leituraForaDaRegra = '^(Status|Consulta[A-Z]|Posicao|Movimento|Cupons|GetUrl|Verificar|Calendario|Recibo)'

$lista = (Invoke-WebRequest -UseBasicParsing "https://developer.omie.com.br/service-list/").Content
$tokens = [regex]::Matches($lista, '(?s)api-categ.*?<strong>(?<grupo>[^<]+)</strong>|href="https://app\.omie\.com\.br//?api/v1/(?<ep>[a-z]+/[a-z0-9]+)/">(?<titulo>[^<]*)</a></td><td>(?<desc>[^<]*)</td>')
$grupo = ""
$servicos = [ordered]@{}
foreach ($t in $tokens) {
    if ($t.Groups["grupo"].Success) { $grupo = Texto $t.Groups["grupo"].Value; continue }
    $ep = $t.Groups["ep"].Value
    if ($servicos.Contains($ep)) { continue }
    $servicos[$ep] = [pscustomobject]@{ Endpoint = $ep; Grupo = $grupo; Titulo = Texto $t.Groups["titulo"].Value; Metodos = @() }
}

foreach ($s in $servicos.Values) {
    $html = (Invoke-WebRequest -UseBasicParsing "https://app.omie.com.br/api/v1/$($s.Endpoint)/").Content
    $s.Metodos = @([regex]::Matches($html, '(?s)<div id="(?<m>\w+)" class="methodItem">\s*<h3 class="method-name">\w+</h3>\s*(?:<p>\s*(?<d>.*?)\s*</p>)?') |
        ForEach-Object { [pscustomobject]@{ Nome = $_.Groups["m"].Value; Descricao = Texto $_.Groups["d"].Value } })
    Write-Host ("{0,-40} {1}" -f $s.Endpoint, $s.Metodos.Count)
    Start-Sleep -Milliseconds 250
}

$todos = @($servicos.Values)
$metodos = @($todos | ForEach-Object { $_.Metodos })
$aceitos = @($metodos | Where-Object { $_.Nome -cmatch $leitura })
$celula = { param($m) $d = $m.Descricao -replace '\|', '/'; if ($d) { "``$($m.Nome)``: $d" } else { "``$($m.Nome)``" } }

$md = New-Object Text.StringBuilder
[void]$md.AppendLine("# What the connector can read in Omie")
[void]$md.AppendLine()
[void]$md.AppendLine("Every service in the Omie API ($($todos.Count) services, $($metodos.Count) methods), taken from Omie's public docs on $(Get-Date -Format 'dd/MM/yyyy') by ``build/catalogo-omie.ps1``. The connector calls $($aceitos.Count) of those methods: the ones whose names start with ``Listar``, ``Pesquisar``, ``Consultar`` or ``Obter``. Everything else is refused before it leaves the PC.")
[void]$md.AppendLine()
[void]$md.AppendLine("Claude reaches any method in the third column through ``consultar`` and ``resumir``, passing the endpoint and the method; the recipes use ``geral/clientes``, ``produtos/nfconsultar`` and ``financas/mf``. A method being listed means the connector accepts the call. It doesn't mean the data exists in every Omie account: a company that doesn't use CRM or service orders gets empty answers there. Omie documents each method's parameters at ``https://app.omie.com.br/api/v1/<endpoint>/``.")
[void]$md.AppendLine()
[void]$md.AppendLine("> **Regra:** a name in this file is not a definition. Before a number from a service becomes a recipe, it has to match the Omie screen (see [receitas.md](receitas.md)).")

foreach ($g in ($todos | Group-Object Grupo)) {
    [void]$md.AppendLine()
    [void]$md.AppendLine("## $($g.Name)")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Endpoint | Service | Methods the connector can call |")
    [void]$md.AppendLine("|---|---|---|")
    foreach ($s in $g.Group) {
        $le = @($s.Metodos | Where-Object { $_.Nome -cmatch $leitura } | ForEach-Object { & $celula $_ })
        $cel = if ($le.Count) { $le -join "<br>" } else { "none (this service only writes)" }
        [void]$md.AppendLine("| ``$($s.Endpoint)`` | $($s.Titulo -replace '\|', '/') | $cel |")
    }
}

$fora = @($todos | ForEach-Object { $s = $_; $s.Metodos | Where-Object { $_.Nome -cnotmatch $leitura -and $_.Nome -cmatch $leituraForaDaRegra } |
    ForEach-Object { "| ``$($s.Endpoint)`` | $(& $celula $_) |" } })
[void]$md.AppendLine()
[void]$md.AppendLine("## Reads the connector refuses today")
[void]$md.AppendLine()
[void]$md.AppendLine("These methods look like reads by name or description but don't follow the prefix rule, so the connector refuses them. Most have an accepted counterpart in the same service (stock position through ``ListarPosEstoque``, stock movements through ``ListarMovimentoEstoque``, an order's status through ``ConsultarPedido``). The fiscal coupon listings in ``produtos/cupomfiscalconsultar`` have none. Any of these would have to be added to the allowlist by exact name, after reading its Omie doc page, before Claude can use it.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Endpoint | Method |")
[void]$md.AppendLine("|---|---|")
$fora | ForEach-Object { [void]$md.AppendLine($_) }

[IO.File]::WriteAllText((Join-Path (Resolve-Path (Split-Path $Saida)) (Split-Path $Saida -Leaf)), $md.ToString(), (New-Object Text.UTF8Encoding $false))
Write-Host "Pronto: $Saida ($($todos.Count) serviços, $($aceitos.Count) métodos aceitos, $($fora.Count) leituras fora da regra)"
