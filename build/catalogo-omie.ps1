# Rebuilds the catalog of every Omie API service, from Omie's public docs (no keys needed). Run it when Omie adds
# services, then rebuild the exe.
#   docs/catalogo-omie.md                     for people: services, the methods the connector can call, the ones it refuses
#   src/OmieMcpReadonly/catalogo-omie.json    built into the server for the catalogo tool: each read method with its
#                                             description, example call and the fields of its request and response types
#   .\build\catalogo-omie.ps1
param(
    [string]$Saida = (Join-Path $PSScriptRoot "..\docs\catalogo-omie.md"),
    [string]$Json = (Join-Path $PSScriptRoot "..\src\OmieMcpReadonly\catalogo-omie.json")
)
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
    $servicos[$ep] = [pscustomobject]@{ Endpoint = $ep; Grupo = $grupo; Titulo = Texto $t.Groups["titulo"].Value
                                        Descricao = Texto $t.Groups["desc"].Value; Metodos = @(); Tipos = @{} }
}

# a page is a run of method blocks, then a run of type blocks; each block ends where the next one starts
$fimDeBloco = '(?=<div id="\w+" class="methodItem">|<div class="complexTypeItem">|\z)'
foreach ($s in $servicos.Values) {
    $html = (Invoke-WebRequest -UseBasicParsing "https://app.omie.com.br/api/v1/$($s.Endpoint)/").Content
    $s.Metodos = @([regex]::Matches($html, "(?s)<div id=""(?<m>\w+)"" class=""methodItem"">\s*<h3 class=""method-name"">\w+</h3>\s*(?:<p>\s*(?<d>.*?)\s*</p>)?(?<corpo>.*?)$fimDeBloco") |
        ForEach-Object {
            $corpo = $_.Groups["corpo"].Value
            $exemplo = [regex]::Match($corpo, '(?s)<pre class="method-example-code">(.*?)</pre>').Groups[1].Value
            [pscustomobject]@{
                Nome = $_.Groups["m"].Value
                Descricao = Texto $_.Groups["d"].Value
                Requisicao = [regex]::Match($corpo, 'method-parameter-type"><a href="#(\w+)"').Groups[1].Value
                Resposta = [regex]::Match($corpo, 'Retorno <span class="pre"><a href="#(\w+)"').Groups[1].Value
                Exemplo = ([Web.HttpUtility]::HtmlDecode(($exemplo -replace '(?i)<br\s*/?>', ' ' -replace '<[^>]+>', '')) -replace '\s+', ' ').Trim()
            } })
    foreach ($t in [regex]::Matches($html, "(?s)<div class=""complexTypeItem"">\s*<a name=""(?<n>\w+)""[^>]*></a>\s*<h3>[^<]*</h3>\s*(?:<p>(?<d>.*?)</p>)?(?<corpo>.*?)$fimDeBloco")) {
        $corpo = $t.Groups["corpo"].Value
        # one row per field; the docs cell is missing on many rows, so no cell may run past its own </td>
        $campos = @([regex]::Matches($corpo, '(?s)<td class="parameter-name (?<c>[^"]*)">(?<n>[^<]*)</td>\s*<td class="parameter-type">(?<t>(?:(?!</td>).)*)</td>(?:<td class="parameter-docs">(?<d>(?:(?!</td>).)*)</td>)?') |
            ForEach-Object {
                $campo = [ordered]@{
                    nome = $_.Groups["n"].Value.Trim()
                    tipo = Texto ($_.Groups["t"].Value -replace '<span class="parameter-lenght">(\d+)</span>', '($1)')
                    descricao = Texto $_.Groups["d"].Value
                    obrigatorio = $_.Groups["c"].Value -notmatch 'optional' }
                $ref = [regex]::Match($_.Groups["t"].Value, 'href="#(\w+)"')
                if ($ref.Success) { $campo.ref = $ref.Groups[1].Value }
                $campo })
        $elemento = [regex]::Match($t.Groups["d"].Value + $corpo, '(?s)array do tipo.*?href="#(\w+)"')
        $s.Tipos[$t.Groups["n"].Value] = [pscustomobject]@{
            Descricao = Texto ($t.Groups["d"].Value + " " + ([regex]::Matches($corpo, '(?s)<p>(.*?)</p>') | ForEach-Object { $_.Groups[1].Value }) -join " ")
            Campos = $campos
            Elemento = if ($elemento.Success) { $elemento.Groups[1].Value } else { $null }
            Refs = @(@($campos | Where-Object { $_.ref } | ForEach-Object { $_.ref }) + @($elemento.Groups[1].Value | Where-Object { $_ }) | Select-Object -Unique)
        }
    }
    Write-Host ("{0,-40} {1} métodos, {2} tipos" -f $s.Endpoint, $s.Metodos.Count, $s.Tipos.Count)
    Start-Sleep -Milliseconds 250
}

$todos = @($servicos.Values)
$metodos = @($todos | ForEach-Object { $_.Metodos })
$aceitos = @($metodos | Where-Object { $_.Nome -cmatch $leitura })
$celula = { param($m) $d = $m.Descricao -replace '\|', '/'; if ($d) { "``$($m.Nome)``: $d" } else { "``$($m.Nome)``" } }

$md = New-Object Text.StringBuilder
[void]$md.AppendLine("# O que o conector consegue ler no Omie")
[void]$md.AppendLine()
[void]$md.AppendLine("Todos os serviços da API do Omie ($($todos.Count) serviços, $($metodos.Count) métodos), tirados da documentação pública do Omie em $(Get-Date -Format 'dd/MM/yyyy') pelo ``build/catalogo-omie.ps1``. O conector chama $($aceitos.Count) desses métodos: os que começam com ``Listar``, ``Pesquisar``, ``Consultar`` ou ``Obter``. Todo o resto é recusado antes de sair do PC.")
[void]$md.AppendLine()
[void]$md.AppendLine("O Claude chega a qualquer método da terceira coluna pelo ``consultar`` e pelo ``resumir``, passando o endpoint e o método; as receitas usam ``geral/clientes``, ``produtos/nfconsultar`` e ``financas/mf``. A mesma lista vem embutida no conector como a ferramenta ``catalogo``, com os parâmetros de cada método e os campos dos registros que ele devolve, para o Claude consultar o serviço em vez de adivinhar. Estar na lista quer dizer que o conector aceita a chamada. Não quer dizer que o dado existe em toda conta do Omie: uma empresa que não usa CRM ou ordens de serviço recebe respostas vazias ali. O Omie documenta os parâmetros de cada método em ``https://app.omie.com.br/api/v1/<endpoint>/``.")
[void]$md.AppendLine()
[void]$md.AppendLine("> **Regra:** um nome neste arquivo não é uma definição. Antes de um número de um serviço virar receita, ele tem que bater com a tela do Omie (ver [receitas.md](receitas.md)).")

foreach ($g in ($todos | Group-Object Grupo)) {
    [void]$md.AppendLine()
    [void]$md.AppendLine("## $($g.Name)")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Endpoint | Serviço | Métodos que o conector pode chamar |")
    [void]$md.AppendLine("|---|---|---|")
    foreach ($s in $g.Group) {
        $le = @($s.Metodos | Where-Object { $_.Nome -cmatch $leitura } | ForEach-Object { & $celula $_ })
        $cel = if ($le.Count) { $le -join "<br>" } else { "nenhum (este serviço só grava)" }
        [void]$md.AppendLine("| ``$($s.Endpoint)`` | $($s.Titulo -replace '\|', '/') | $cel |")
    }
}

$fora = @($todos | ForEach-Object { $s = $_; $s.Metodos | Where-Object { $_.Nome -cnotmatch $leitura -and $_.Nome -cmatch $leituraForaDaRegra } |
    ForEach-Object { "| ``$($s.Endpoint)`` | $(& $celula $_) |" } })
[void]$md.AppendLine()
[void]$md.AppendLine("## Leituras que o conector recusa hoje")
[void]$md.AppendLine()
[void]$md.AppendLine("Estes métodos parecem leitura pelo nome ou pela descrição, mas não seguem a regra do prefixo, então o conector recusa. A maioria tem um equivalente aceito no mesmo serviço (posição de estoque pelo ``ListarPosEstoque``, movimentos de estoque pelo ``ListarMovimentoEstoque``, situação de um pedido pelo ``ConsultarPedido``). As listagens de cupom fiscal em ``produtos/cupomfiscalconsultar`` não têm. Para o Claude poder usar qualquer um deles, ele teria que entrar na lista de permitidos pelo nome exato, depois de ler a página dele na documentação do Omie.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Endpoint | Método |")
[void]$md.AppendLine("|---|---|")
$fora | ForEach-Object { [void]$md.AppendLine($_) }

[IO.File]::WriteAllText((Join-Path (Resolve-Path (Split-Path $Saida)) (Split-Path $Saida -Leaf)), $md.ToString(), (New-Object Text.UTF8Encoding $false))

# the JSON keeps only what a read needs: accepted methods, and the types reachable from their requests and responses
$catalogo = foreach ($s in $todos) {
    $le = @($s.Metodos | Where-Object { $_.Nome -cmatch $leitura })
    $fila = New-Object Collections.Generic.Queue[string]
    $le | ForEach-Object { $_.Requisicao; $_.Resposta } | Where-Object { $_ } | ForEach-Object { $fila.Enqueue($_) }
    $tipos = [ordered]@{}
    while ($fila.Count) {
        $n = $fila.Dequeue()
        if ($tipos.Contains($n)) { continue }
        if (-not $s.Tipos.ContainsKey($n)) {
            # "nfCadastroArray" is often not defined on the page: it is a list of nfCadastro
            if ($n -cmatch '^(?<e>\w+)Array$' -and $s.Tipos.ContainsKey($matches.e)) { $tipos[$n] = [ordered]@{ descricao = "Lista de $($matches.e)"; elemento = $matches.e; campos = @() }; $fila.Enqueue($matches.e) }
            continue
        }
        $t = $s.Tipos[$n]
        $tipos[$n] = [ordered]@{ descricao = $t.Descricao; campos = @($t.Campos) }
        if ($t.Elemento) { $tipos[$n].elemento = $t.Elemento }
        $t.Refs | ForEach-Object { $fila.Enqueue($_) }
    }
    [ordered]@{
        endpoint = $s.Endpoint; grupo = $s.Grupo; titulo = $s.Titulo; descricao = $s.Descricao
        metodos = @($le | ForEach-Object { [ordered]@{ nome = $_.Nome; descricao = $_.Descricao; requisicao = $_.Requisicao; resposta = $_.Resposta; exemplo = $_.Exemplo } })
        recusados = @($s.Metodos | Where-Object { $_.Nome -cnotmatch $leitura } | ForEach-Object { $_.Nome })
        tipos = $tipos
    }
}
$doc = [ordered]@{ gerado_em = (Get-Date -Format "dd/MM/yyyy"); fonte = "https://developer.omie.com.br/service-list/"; servicos = @($catalogo) }
[IO.File]::WriteAllText((Join-Path (Resolve-Path (Split-Path $Json)) (Split-Path $Json -Leaf)), ($doc | ConvertTo-Json -Depth 12 -Compress), (New-Object Text.UTF8Encoding $false))
Write-Host "Pronto: $Saida ($($todos.Count) serviços, $($aceitos.Count) métodos aceitos, $($fora.Count) leituras fora da regra)"
