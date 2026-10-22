# O que o conector consegue ler no Omie

Todos os serviços da API do Omie (134 serviços, 466 métodos), tirados da documentação pública do Omie em 22/10/2026 pelo `build/catalogo-omie.ps1`. O conector chama 201 desses métodos: os que começam com `Listar`, `Pesquisar`, `Consultar` ou `Obter`. Todo o resto é recusado antes de sair do PC.

O Claude chega a qualquer método da terceira coluna pelo `consultar` e pelo `resumir`, passando o endpoint e o método; as receitas usam `geral/clientes`, `produtos/nfconsultar` e `financas/mf`. A mesma lista vem embutida no conector como a ferramenta `catalogo`, com os parâmetros de cada método e os campos dos registros que ele devolve, para o Claude consultar o serviço em vez de adivinhar. Estar na lista quer dizer que o conector aceita a chamada. Não quer dizer que o dado existe em toda conta do Omie: uma empresa que não usa CRM ou ordens de serviço recebe respostas vazias ali. O Omie documenta os parâmetros de cada método em `https://app.omie.com.br/api/v1/<endpoint>/`.

> **Regra:** um nome neste arquivo não é uma definição. Antes de um número de um serviço virar receita, ele tem que bater com a tela do Omie (ver [receitas.md](receitas.md)).

## Geral

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `geral/clientes` | Clientes, Fornecedores, Transportadoras, etc | `ConsultarCliente`: Consulta os dados de um cliente<br>`ListarClientes`: Lista os clientes cadastrados<br>`ListarClientesResumido`: Realiza pesquisa dos clientes |
| `geral/clientescaract` | Clientes - Características | `ConsultarCaractCliente` |
| `geral/clientetag` | Tags | `ListarTags`: Lista as tags de um determinado cliente. |
| `geral/projetos` | Projetos | `ConsultarProjeto`: Consulta um projeto<br>`ListarProjetos`: Lista os projetos cadastrados |
| `geral/empresas` | Empresas | `ConsultarEmpresa`: Realiza a consulta de um registro especifico<br>`ListarEmpresas`: Lista as empresas cadastradas no Omie. |
| `geral/departamentos` | Departamentos | `ConsultarDepartamento`<br>`ListarDepartamentos`: Lista os departamentos cadastrados no Omie.<br>`ListarDepatartamentos`: DEPRECATED |
| `geral/categorias` | Categorias | `ConsultarCategoria`: Consulta uma categoria.<br>`ListarCategorias`: Lista as categorias cadastradas. |
| `geral/parcelas` | Parcelas | `ListarParcelas`: Lista de Parcelas cadastradas. |
| `geral/tpativ` | Tipos de Atividade da Empresa | `ListarTipoAtiv`: Listar Tipos de Atividade. |
| `produtos/cnae` | CNAE | `ListarCNAE`: Listar os CNAEs cadastrados. |
| `geral/cidades` | Cidades | `PesquisarCidades` |
| `geral/paises` | Países | `ListarPaises` |
| `geral/tiposanexo` | Tipos de Anexos | `ListarTiposAnexos`: Listagem de tipos de anexos. |
| `geral/anexo` | Documentos Anexos | `ConsultarAnexo`: Consulta os dados de um anexo vinculado a um documento.<br>`ListarAnexo`: Lista os anexos vinculados a um documento.<br>`ObterAnexo` |
| `geral/tiposentrega` | Tipo de Entrega | `ConsultarTipoEntrega`: Consultar tipo de entrega<br>`ListarTipoEntrega`: Listar tipo de entrega |
| `geral/tipoassinante` | Tipo de Assinante | `ListarTipoAssinante`: Lista os tipos de assinante |
| `geral/tarefas` | Tarefas | `ConsultarTarefa`<br>`ListarTarefas`<br>`ObterCalendarioTarefas`<br>`ObterResumoTarefas`<br>`ObterTotalTarefas` |
| `produtos/icbscst` | IBS e CBS - CST | `ListarCSTIbsCbs`: Listagem das CST's do IBS e CBS. |
| `produtos/classtrib` | IBS e CBS - CSL | `ListarClassTrib` |

## CRM

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `crm/contas` | Contas | `ConsultarConta`: Consulta uma conta do CRM.<br>`ListarContas`: Lista as contas do CRM. |
| `crm/contascaract` | Contas - Características | `ConsultarCaractConta` |
| `crm/contatos` | Contatos | `ConsultarContato`: Consulta o Contato<br>`ListarContatos`: Lista os contatos do CRM. |
| `crm/oportunidades` | Oportunidades | `ConsultarOportunidade`: Consulta de oportunidade.<br>`ListarOportunidades`: Lista de oportunidades. |
| `crm/tarefas` | Tarefas | `ConsultarTarefa`: Consulta uma tarefa da oportunidade.<br>`ListarEmailsTarefas`: Lista os emails tarefas.<br>`ListarTarefas`: Tarefas da oportunidade. |
| `crm/solucoes` | Soluções | `ListarSolucoes` |
| `crm/fases` | Fases | `ListarFases`: Fases da Oportunidade |
| `crm/usuarios` | Usuários | `ListarUsuarios`<br>`ObterUsuarios` |
| `crm/status` | Status | `ListarStatus`: Status da oportunidade. |
| `crm/motivos` | Motivos | `ListarMotivos`: Motivos da oportunidade. |
| `crm/tipos` | Tipos | `ListarTipos`: Tipos de oportunidades. |
| `crm/parceiros` | Parceiros | `ListarParceiros`: Parceiros e equipes da oportunidade. |
| `crm/finders` | Finders | `ListarFinders`: Finders da oportunidade. |
| `crm/origens` | Origens | `ListarOrigens`: Origens da oportunidade. |
| `crm/concorrentes` | Concorrentes | `ListarConcorrentes`: Concorrentes da oportunidade. |
| `crm/verticais` | Verticais | `ListarVerticais`: Lista as verticais cadastradas. |
| `crm/tipostarefa` | Tipos de Tarefas | `ConsultarTipoTarefa`: Consulta tipo de tarefa.<br>`ListarTiposTarefa`: Lista tipos de tarefa. |

## Finanças

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `geral/contacorrente` | Contas Correntes | `ConsultarContaCorrente`: Realiza a consulta de uma conta corrente<br>`ListarContasCorrentes`: Listar Contas Correntes<br>`ListarResumoContasCorrentes`: Listar resumida de Contas correntes.<br>`PesquisarContaCorrente`: DEPRECATED |
| `financas/contacorrentelancamentos` | Contas Correntes - Lançamentos | `ListarLancCC` |
| `financas/contapagar` | Contas a Pagar - Lançamentos | `ConsultarContaPagar`: Consulta uma conta a pagar<br>`ListarContasPagar`: Listar as Contas a Pagar |
| `financas/contareceber` | Contas a Receber - Lançamentos | `ConsultarContaReceber`: Consulta uma Conta a Receber<br>`ListarContasReceber`: Lista as contas a receber cadastradas. |
| `financas/contareceberboleto` | Contas a Receber - Boletos | `ObterBoleto` |
| `financas/pix` | Contas a Receber - PIX | `ListarPix`<br>`ListarStatusPix`<br>`ObterPix`<br>`ObterStatusPix` |
| `financas/extrato` | Extrato de Conta Corrente | `ListarExtrato`: Listagem do Extrato |
| `financas/caixa` | Orçamento de Caixa | `ListarOrcamentos` |
| `financas/pesquisartitulos` | Pesquisar Títulos | `ObterURLBoleto`: DEPRECATED<br>`PesquisarExcluidos`: DEPRECATED<br>`PesquisarLancamentos` |
| `financas/mf` | Movimentos Financeiros | `ListarMovimentos` |
| `financas/resumo` | Resumo | `ObterDetalhesLancamento`<br>`ObterListaEmAberto`<br>`ObterListaFinancas`<br>`ObterResumoFinancas` |
| `geral/bancos` | Bancos | `ConsultarBanco`<br>`ListarBancos`: Exibe uma lista com os banco cadastrados. |
| `geral/tiposdoc` | Tipos de Documento | `ConsultarTipoDocumento`<br>`PesquisarTipoDocumento`: Pesquisa o tipo de documento |
| `geral/tipocc` | Tipos de Contas Correntes | `ListarTiposCC`: Listar os tipos de contas correntes. |
| `geral/dre` | Contas do DRE | `ListarCadastroDRE`: Lista as contas do DRE |
| `geral/finaltransf` | Finalidade de Transferência | `ConsultarFinalTransf`<br>`ListarFinalTransf` |
| `geral/origemlancamento` | Origem do títulos | `ListarOrigem` |
| `geral/bandeiracartao` | Bandeiras de Cartão | `ListarBandeiras` |

## Compras, Estoque e Produção

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `geral/produtos` | Produtos | `ConsultarProduto`: Consulta um produto.<br>`ListarProdutos`: Lista completa do Cadastro de Produtos<br>`ListarProdutosResumido`: Lista os produtos cadastrados |
| `geral/prodcaract` | Produtos - Características | `ConsultarCaractProduto`<br>`ListarCaractProduto` |
| `geral/malha` | Produtos - Estrutura | `ConsultarEstrutura`: Consulta a estrutura do produto.<br>`ListarEstruturas`: Lista as estruturas de produtos. |
| `geral/produtoskit` | Produtos - Kit | nenhum (este serviço só grava) |
| `produtos/variacao` | Produtos - Variação | `ConsultarVariacoes`<br>`ListarVariacoes` |
| `produtos/produtoslote` | Produtos - Lote | `ConsultarLote`<br>`ListarLotes` |
| `produtos/requisicaocompra` | Requisições de Compra | `ConsultarReq`<br>`PesquisarReq` |
| `produtos/pedidocompra` | Pedidos de Compra | `ConsultarPedCompra`<br>`ListarMotivosEncerramento`<br>`PesquisarPedCompra` |
| `produtos/op` | Ordens de Produção | `ConsultarOrdemProducao`<br>`ListarOrdemProducao` |
| `produtos/notaentrada` | Nota de Entrada | `ConsultarNotaEnt`: Consultar nota de entrada.<br>`ListarNotaEnt`: Listagem de nota de entrada. |
| `produtos/notaentradafat` | Nota de Entrada - Faturamento | nenhum (este serviço só grava) |
| `produtos/recebimentonfe` | Recebimento de Nota Fiscal | `ConsultarRecebimento`: Consultar recebimento de NFe.<br>`ListarRecebimentos`: Listar recebimento de NFe. |
| `geral/familias` | Familias de Produto | `ConsultarFamilia`: Consulta uma familia de produto.<br>`PesquisarFamilias`: Listagem de familias cadastradas. |
| `geral/unidade` | Unidades | `ListarUnidades`: Lista as unidades de medidas. |
| `estoque/comprador` | Compradores | `ListarCompradores`: Lista os compradores cadastrados. |
| `estoque/produtofornecedor` | Produto x Fornecedor | `ListarProdutoFornecedor`: Listar os produtos por fornecedores. |
| `produtos/formaspagcompras` | Formas de Pagamento | `ListarFormasPagCompras`: Listar formas de Pagamento de Compras. |
| `produtos/ncm` | NCM | `ConsultarNCM`: Consulta um NCM.<br>`ListarNCM`: Lista os NCMs cadastrados. |
| `geral/cenarios` | Cenário de Impostos | `ListarCenarios`<br>`ListarImpostosCenario` |
| `produtos/cfop` | CFOP | `ListarCFOP`: Listar as CFOPs |
| `produtos/icmscst` | ICMS - CST | `ListarCST`: Listar os CSTs do ICMS. |
| `produtos/icmscsosn` | ICMS - CSOSN | `ListarCSOSN`: Listar os CSOSN do ICMS. |
| `produtos/icmsorigem` | ICMS - Origem da Mercadoria | `ListarOrigMerc`: Listar a origem da mercadoria do ICMS. |
| `produtos/piscst` | PIS - CST | `ListarCstPis`: Listar o CST do PIS. |
| `produtos/cofinscst` | COFINS - CST | `ListarCstCofins`: Listar CST do COFINS. |
| `produtos/ipicst` | IPI - CST | `ListarCstIpi`: Listar CST do IPI. |
| `produtos/ipienq` | IPI - Enquadramento | `ListarEnqIpi`: Listar Enquadramento do IPI. |
| `produtos/tpcalc` | Tipo de Cálculo | `ListarTpCalc` |
| `produtos/cest` | CEST | `ListarCEST`: Listar CEST. |
| `estoque/ajuste` | Ajustes de Estoque | `ListarAjusteEstoque`: Listar os ajuste de estoque. |
| `estoque/consulta` | Consulta Estoque | `ListarMovimentoEstoque`<br>`ListarPosEstoque`<br>`ListarSaldoPendente`: Lista o saldo pendente de estoque. |
| `estoque/movestoque` | Movimento Estoque | `ConsultarPrevisao`<br>`ListarMovimentos` |
| `estoque/local` | Locais de Estoque | `ListarLocaisEstoque`: Lista os Locais de Estoque encontrados. |
| `estoque/resumo` | Resumo do Estoque | `ObterEstoqueProduto` |

## Vendas e NF-e

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `produtos/pedidovenda` | Pedidos de Venda - Resumido | nenhum (este serviço só grava) |
| `produtos/pedido` | Pedidos de Venda | `ConsultarPedido`: Consulta de Pedido de Venda de Produto<br>`ListarPedidos`: Listar os pedidos de venda de produto |
| `produtos/pedidovendafat` | Pedidos de Venda - Faturamento | `ObterPedidosVenda`: Retorna a lista de pedidos de venda a serem faturados. |
| `produtos/pedidoetapas` | Pedidos de Venda - Etapas | `ListarEtapasPedido`: Lista as etapas do Pedido de Venda de Produtos. |
| `produtos/cte` | CT-e / CT-e OS | `ListarNFeTransp` |
| `produtos/remessa` | Remessa de Produtos | `ConsultarRemessa`: Consulta uma remessa.<br>`ListarRemessas`: Lista as remessas cadastradas. |
| `produtos/remessafat` | Remessa de Produtos - Faturamento | nenhum (este serviço só grava) |
| `produtos/devolucaovendafaturamento` | Devolução de venda - Faturamento | nenhum (este serviço só grava) |
| `produtos/dfedocs` | Obter Documentos | `ObterCTe`<br>`ObterCupom`<br>`ObterDanfeSimp`<br>`ObterNfe`<br>`ObterPedVenda` |
| `produtos/cupomfiscalincluir` | Adicionar | nenhum (este serviço só grava) |
| `produtos/cupomfiscal` | Cancelar ou excluir | `ListarCupons`: Lista os Cupons Fiscais.<br>`ObterProximoLote` |
| `produtos/cupomfiscalconsultar` | Consultar | nenhum (este serviço só grava) |
| `produtos/nfce` | Importar NFC-e | nenhum (este serviço só grava) |
| `produtos/sat` | Importar CFe-Sat | nenhum (este serviço só grava) |
| `geral/vendedores` | Vendedores | `ConsultarVendedor`: Consulta os dados de um vendedor<br>`ListarVendedores`: Listagem de Vendedores |
| `produtos/formaspagvendas` | Formas de Pagamento | `ListarFormasPagVendas`: Lista as formas de pagamento de vendas. |
| `produtos/tabelaprecos` | Tabela de Preços | `ConsultarTabelaPreco`<br>`ListarTabelaItens`<br>`ListarTabelasPreco` |
| `geral/caracteristicas` | Características de produtos | `ConsultarCaracteristica`<br>`ListarCaracteristicas` |
| `produtos/etapafat` | Etapas de Faturamento | `ListarEtapasFaturamento`: Lista as etapas do faturamento |
| `geral/meiospagamento` | Meios de Pagamento | `ListarMeiosPagamento`: Listagem de meios de pagamento. |
| `geral/origempedido` | Origem do Pedido | `ListarOrigem`: Lista Origem de pedidos. |
| `geral/motivodevolucao` | Motivos de Devolução | `ListarMotivosDevol` |
| `produtos/nfconsultar` | Consultas | `ConsultarNF`: Consulta uma Nota Fiscal.<br>`ListarNF`: Listar as Notas Fiscais cadastradas. |
| `produtos/notafiscalutil` | Utilitários | nenhum (este serviço só grava) |
| `produtos/nfe` | Importar | `ListarNFe`: Lista as NFes importadas. |

## Serviços e NFS-e

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `servicos/servico` | Serviços | `ConsultarCadastroServico`<br>`ListarCadastroServico` |
| `servicos/os` | Ordens de Serviço | `ConsultarOS`<br>`ListarOS` |
| `servicos/osp` | Ordens de Serviço - Faturamento | `ObterOS` |
| `servicos/oslote` | Ordens de Serviço - Fat. em Lote | `ListarLoteNfse`<br>`ListarLotesOS` |
| `servicos/contrato` | Contratos de Serviço | `ConsultarContrato`<br>`ListarContratos`: Lista os contratos cadastrados. |
| `servicos/contratofat` | Contratos de Serviço - Faturamento | `ObterContratos` |
| `servicos/contratolote` | Contratos de Serviço - Fat. em Lote | `ListarLotesContrato` |
| `servicos/resumo` | Resumo | `ObterResumoServicos` |
| `servicos/osdocs` | Obter Documentos | `ObterDemonst`<br>`ObterNFSe`<br>`ObterOS`<br>`ObterRPS`<br>`ObterRecibo`<br>`ObterViaUnica` |
| `servicos/nfse` | Consultas | `ListarNFSEs`: Lista de NFS-es. |
| `servicos/listaservico` | Serviços no Município | `ListarServMunic` |
| `servicos/tipotrib` | Tipos de Tributação | `ListarTiposTrib` |
| `servicos/lc116` | LC 116 | `ListarLC116` |
| `servicos/nbs` | NBS | `ListarNBS` |
| `servicos/ibpt` | IBPT | `ListarProdutosIBPT`: Lista os produtos da Tabela do IBPT.<br>`ListarServicosIBPT` |
| `servicos/contratotpfat` | Tipo de Faturamento de Contrato | `ListarTipoFatContrato`: Lista os tipos de faturamento de contrato. |
| `servicos/tipoutilizacao` | Tipo de utilização | `ListarTipoUtilizacao` |
| `servicos/classificacaoservico` | Classificação do Serviço | `ListarClassificacaoServico` |
| `servicos/indoper` | Indicador de Operação | `ListarIndOper` |

## Painel do Contador

| Endpoint | Serviço | Métodos que o conector pode chamar |
|---|---|---|
| `contador/xml` | Documentos Fiscais | `ListarDocumentos`: Lista de XMLs de Documentos Fiscais. |
| `contador/resumo` | Resumo | `ObterResumoContador`: Obtem o resumo do painel do contador. |

## Leituras que o conector recusa hoje

Estes métodos parecem leitura pelo nome ou pela descrição, mas não seguem a regra do prefixo, então o conector recusa. A maioria tem um equivalente aceito no mesmo serviço (posição de estoque pelo `ListarPosEstoque`, movimentos de estoque pelo `ListarMovimentoEstoque`, situação de um pedido pelo `ConsultarPedido`). As listagens de cupom fiscal em `produtos/cupomfiscalconsultar` não têm. Para o Claude poder usar qualquer um deles, ele teria que entrar na lista de permitidos pelo nome exato, depois de ler a página dele na documentação do Omie.

| Endpoint | Método |
|---|---|
| `crm/contas` | `VerificarConta`: Verifica se uma conta foi criada a partir do nome e e-mail. |
| `crm/contatos` | `VerificarContato` |
| `crm/tarefas` | `CalendarioTarefas` |
| `financas/contacorrentelancamentos` | `ConsultaLancCC` |
| `financas/contapagar` | `ReciboContaPagar`: Recibo de uma conta a pagar. |
| `produtos/notaentrada` | `StatusNotaEnt`: Status de nota de entrada. |
| `estoque/consulta` | `ConsultaSaldoProduto` |
| `estoque/consulta` | `MovimentoEstoque`: Consulta do Movimento de Estoque. |
| `estoque/consulta` | `PosicaoEstoque` |
| `produtos/pedido` | `StatusPedido`: Consulta do Status do Pedido |
| `produtos/cte` | `StatusFatura`: Retorna o Status da Fatura inclusa. |
| `produtos/remessa` | `StatusRemessa`: Retorna o status da remessa. |
| `produtos/devolucaovendafaturamento` | `StatusDevolucaoVenda` |
| `produtos/cupomfiscalconsultar` | `CuponsFiscais`: Listagem dos cupons fiscais. |
| `produtos/cupomfiscalconsultar` | `CuponsItens`: Listagem dos itens dos cupons fiscais. |
| `produtos/cupomfiscalconsultar` | `CuponsPagamentos`: Listagem dos pagamentos dos cupons fiscais. |
| `produtos/notafiscalutil` | `GetUrlDanfe` |
| `produtos/notafiscalutil` | `GetUrlLogo`: Retorna a URL do Logotipo. |
| `produtos/notafiscalutil` | `GetUrlNotaFiscal`: Retorna a URL da Nota Fiscal. |
| `servicos/os` | `StatusOS` |
| `servicos/oslote` | `StatusLoteOS`: Status do lote faturado a partir do ID. |
| `servicos/contratolote` | `StatusLoteContrato`: Status do lote faturado a partir do ID. |
