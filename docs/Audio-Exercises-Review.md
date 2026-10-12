# Revisão final dos exercícios de Áudio

**Data:** 12/10/2026. **Base inicial:** `feat/boost-cut-console-pr163`, `e754807` — arquivos locais, não produção.
**Verificação concorrente:** o ambiente mudou externamente para `feat/boost-cut-console-master`, `621901a`; nenhuma operação de branch foi feita nesta revisão. O núcleo de Áudio/trilha/UI não mudou, mas sete badges de Áudio foram incorporados e estão considerados abaixo.
**Destinatário:** responsável pelo produto. **Entrega original:** decisões e propostas, não implementação.
**Continuidade:** o status das entregas posteriores está no [acompanhamento da implementação](#acompanhamento-da-implementação); o restante do relatório preserva o retrato da revisão original.

## Acompanhamento da implementação

**Atualização:** 12/10/2026. **Base da implementação:** `master`, `e4096fc`. **Branch:** `feat/audio-catalog-categories`.

| Fase | Estado | Entrega ou próxima decisão |
|---|---|---|
| A — documentação sincronizada | Pendente | Este incremento atualiza o registro de categorias; ainda falta sincronizar os parâmetros de Boost or Cut e Guess the Q. |
| B — categorias visíveis | **Implementada nesta branch** | Aba **Por categoria**, badges nos cards dos três agrupamentos e remoção de seções vazias de categoria/tipo. Áudio exibe suas seis áreas na ordem de `TrainingTracks.Audio`; Música mantém suas categorias e URLs. |
| C — filtros combináveis | Pendente | Combinar categoria/tipo/nível por GET e contar exercícios únicos; não faz parte da entrega B. |
| D — validade EQ Matching | Aguarda decisão de produto | Definir exigência de direção e pisos por componente antes de alterar `IsCorrect`; preservar o histórico. |
| E a N | Não iniciadas | Seguir as dependências e critérios da seção 7; não ampliar DSP, trilha ou conteúdo neste incremento. |

A fase B reutiliza as chaves e FKs existentes, mantém os filtros de nível e os links de entrada padrão e não altera DSP, pontuação, trilha, badges de conquista ou schema. Os nomes visuais e a nova aba estão em en-US, pt-BR e fr-CA. Cards compartilham `_ExerciseCatalogCard.cshtml`, para que categoria, subtítulo e links permaneçam consistentes nos três agrupamentos.

**Verificação executada:** build Release com 22 testes unitários e 44 de integração direcionados aprovados; dois cenários Playwright aprovados no Edge com SQL local temporário. A cobertura inclui as três culturas, Música/Áudio, agrupamento único, FKs de bases existentes, seções vazias, nomes legados sem rótulo de exibição, teclado, temas claro/escuro a 360 px e os links de nível existentes em 360/768/1024/1280 px. O banco temporário foi removido. Isso não é uma avaliação de escuta nem substitui a CI completa do PR.

O Gitleaks interpretou a expressão descritiva `round/private` na linha `TOKEN` do apêndice como chave de API. A descrição atual foi reescrita; somente as duas ocorrências históricas já publicadas foram registradas por fingerprint na baseline. Nenhuma credencial foi encontrada nessa descrição, e nenhum padrão global de exclusão foi ampliado. A validação local com Gitleaks 8.30.1 foi aprovada para o histórico completo e a versão atual do relatório.

**Para retomar:** este arquivo e a implementação da fase B seguem juntos no PR da branch acima. A correção documental restante da fase A pode sair de forma independente; a fase C pode continuar sobre B; a fase D ainda exige a decisão de produto indicada na seção 8. Não há merge ou deploy incluído nesta entrega.

## Como usar este relatório

- **Fato:** comportamento encontrado em código, recursos ou testes existentes.
- **Recomendação:** avaliação pedagógica ou proposta; não significa funcionalidade entregue.
- **Pendente de escuta:** hipótese que precisa de avaliação com pessoas e equipamento real.
- Referências como **GEN:27–62** apontam para arquivos e linhas identificados no [Apêndice](#apêndice-índice-de-arquivos-e-linhas).
- Os nomes dos exercícios e identificadores permanecem no original; os comentários estão em pt-BR.
- **S:** PR pequeno, sem novo DSP/modelo; **M:** várias camadas ou componente compartilhado; **L:** novo DSP, fontes, persistência ou experiência substancial.
- **P0:** validade do treino/contrato antes de ampliar o recurso; **P1:** próximo ciclo; **P2:** evolução; **P3:** exploração.
- As estimativas são relativas: incluem localização e testes, mas não aprovação/licenciamento de novas gravações.

### Escopo e limites da verificação

- Foram lidos o README externo autorizado, com **945 linhas**, `docs\Audio-Ear-Training.md` e `docs\Backlog.md` integralmente.
- O inventário foi cruzado com `AudioComparisonCatalog`, `SeedData`, geradores, validadores, rotas e trilha.
- As cinco views que efetivamente atendem Áudio, o ramo especial de Boost or Cut, seus scripts e os estilos compartilhados foram examinados.
- Os recursos foram inspecionados nas três culturas; a existência de nomes, instruções, subtítulos e três rótulos de nível foi conferida estaticamente para os 27 exercícios.
- Foram lidos testes relevantes, **sem executá-los**. Teste existente não equivale a teste aprovado neste checkout.
- Não houve execução da aplicação, reprodução de áudio, consulta ao banco/produção ou operação sobre o dev server.
- Não foram alterados código, documentação preexistente nem `Directory.Packages.props`.

### Navegação

1. [Resumo executivo](#1-resumo-executivo)
2. [Inventário atual](#2-inventário-atual-dos-exercícios-de-áudio)
3. [Layouts mais realistas](#3-layouts-mais-realistas)
4. [Comparação com o documento de referência](#4-comparação-com-o-readme-audio-ear-trainingmd)
5. [Categorias](#5-categorias-de-exercícios)
6. [Níveis](#6-revisão-de-níveis)
7. [Roadmap](#7-roadmap-sugerido)
8. [Riscos e decisões](#8-riscos-e-perguntas-em-aberto)

## 1. Resumo executivo

### Dez recomendações, ordenadas por valor/esforço

| Ordem | Recomendação | Prioridade / esforço | Motivo em uma linha |
|---|---|---|---|
| 1 | Expor as **seis categorias já existentes** no catálogo de Áudio | P1 / S | Resolve descoberta dos 27 exercícios sem criar coluna, migration ou nova taxonomia incompatível. |
| 2 | Atualizar o registro técnico de Boost or Cut e Guess the Q | P1 / S | O documento interno ainda descreve três regiões/seis respostas e outro Q avançado; o código já mudou. |
| 3 | Decidir um piso de qualidade por parâmetro em EQ Matching | P0 / S–M | Hoje uma curva sem ganho ou até com direção errada pode superar 80%; o cálculo está correto, a regra merece revisão. |
| 4 | Simplificar Boost or Cut iniciante e separar melhor os centros graves | P0 / M | Sete regiões, incluindo 100 e 125 Hz com Q 1, não atendem bem à promessa de poucas escolhas bem distintas. |
| 5 | Introduzir feedback visual **somente depois da validação** | P1 / M | Curvas, envelopes e comparação do palpite ensinam sem transformar a rodada em leitura de instrumentos. |
| 6 | Consolidar knob/fader acessível e temas dos dois consoles existentes | P1 / M | Reaproveita trabalho entregue; hoje os painéis têm paletas próprias escuras, não variantes claras/escuras. |
| 7 | Oferecer uma referência espectral fixa no início de Guess the Q | P1 / S–M | Isola largura de banda antes de exigir que o aluno generalize entre 500, 1.000 e 2.000 Hz. |
| 8 | Calibrar Reverb mix e a paridade dos níveis de Channel Polarity | P1 / M | A opção dry fica mais distante no avançado; o modo bundled de polarity não muda de material por nível. |
| 9 | Qualificar fontes e registrar uma revisão de escuta dos extremos | P0 / M–L | Segurança e diferença numérica não demonstram audibilidade útil, especialmente em EQ Matching avançado. |
| 10 | Evoluir diagnóstico para Mono Compatibility/Masking e Mix Challenges por etapas | P2 / L | São lacunas de habilidade relevantes; rendem mais que duplicar exercícios de boost/cut com novos nomes. |

**Conclusão:** o catálogo já é amplo; a próxima melhoria deve ser **coerência pedagógica, descoberta e feedback**, não multiplicação de páginas.

## 2. Inventário atual dos exercícios de Áudio

### Contagem e cobertura

| Origem no código | Quantidade | Evidência |
|---|---:|---|
| Foundations: LevelMatch, StereoPosition, GuessFrequency | 3 | GEN:82–96; SEED:1605–1689 |
| BoostOrCut, GuessQ, Compression | 3 | CAT:40–46 |
| `AudioComparisonCatalog.Expansion` | 20 | CAT:8–30 |
| `AudioComparisonCatalog.Continuous`: EqMatching | 1 | CAT:33–36 |
| **Total de Áudio** | **27** | 3 + `AudioComparisonCatalog.All` com 24 definições |

- A contagem coincide com `docs\Audio-Ear-Training.md:5–7` e com os 27 passos em PATH.
- `All` **já contém** `Continuous`; somar `All + Continuous` contaria EQ Matching duas vezes.
- `All` não contém o trio Foundations; ignorá-lo reduziria o inventário indevidamente para 24.
- Aliases do README, como Pan Position/Stereo Position e Attack & Release/dois exercícios, não são novos exercícios no código.
- Os 23 comparativos discretos do catálogo usam a mesma view; Boost or Cut tem um ramo próprio nela. Outros 22 conservam o painel genérico. [CTRL:1127–1159; VIEW:24–116]

### Inventário completo

**Níveis:** B/I/A = beginner/intermediate/advanced; parâmetros exatos estão na seção 6.
**Layout:** “genérico” = painel de pergunta, transporte A/B e botões; não uma interface de processador.
**Trilha:** número é a posição atual, não uma recomendação de dificuldade intrínseca. [PATH:68–94]

| # | Nome / identificador | O que treina | Dinâmica de resposta | Layout hoje | Níveis / filtro | Posição atual |
|---|---|---|---|---|---|---:|
| 1 | Level Match / `LevelMatch` | Nível relativo e diferença em dB | A/B; no I/A também diferença discreta | A/B + dois grupos de botões | B/I/A, `lmLevel` | 1 |
| 2 | Stereo Position / `StereoPosition` | Pan por diferença de nível | Código de posição discreta | Um áudio + posições em botões + aviso de equipamento | B/I/A, `spLevel` | 2 |
| 3 | Guess Frequency / `GuessFrequency` | Centro de um boost paramétrico | Uma entre quatro frequências sorteadas | A/B + quatro botões dinâmicos | B/I/A, `gfLevel` | 3 |
| 4 | Boost or Cut / `BoostOrCut` | Direção e região de EQ | Direção + região, código combinado | **Console entregue**, seletor boost/cut + sete bandas | B/I/A, `bcLevel` | 4 |
| 5 | Guess the Q / `GuessQ` | Largura de banda | Broad/medium/narrow; cinco Q no A | Genérico; não tem knob/curva própria | B/I/A, `gqLevel` | 5 |
| 6 | Compression / `Compression` | Detecção de compressão adicionada | Escolher A ou B | Genérico, lado comprimido aleatório | B/I/A, `cpLevel` | 6 |
| 7 | Compressor Attack / `CompressorAttack` | Rapidez de ataque | Qual lado tem ataque mais rápido | Genérico, ambos comprimidos | B/I/A, `caLevel` | 7 |
| 8 | Compressor Release / `CompressorRelease` | Recuperação entre transientes | Qual lado tem release mais rápido | Genérico, ambos comprimidos | B/I/A, `crLevel` | 8 |
| 9 | Compression Ratio / `CompressionRatio` | Intensidade relativa de ratio | Light/medium/strong do perfil | Genérico, A é referência | B/I/A, `raLevel` | 9 |
| 10 | Stereo Width / `StereoWidth` | Quantidade relativa de Side | Narrow/same/wide | Genérico; aviso de estéreo nas instruções | B/I/A, `swLevel` | 10 |
| 11 | Channel Polarity / `ChannelPolarity` | Inversão relativa do canal direito | Normal/inverted | Genérico; não há botão mono | B/I/A, `poLevel` | 11 |
| 12 | Hear the Reverb / `ReverbPresence` | Reverb adicionado à referência | Escolher A ou B | Genérico, lado aleatório | B/I/A, `rvLevel` | 12 |
| 13 | Reverb Decay / `ReverbDecay` | Cauda nominal curta/longa | Qual lado tem decay maior | Genérico | B/I/A, `dcLevel` | 13 |
| 14 | Reverb Pre-delay / `ReverbPreDelay` | Separação temporal das reflexões | Qual lado tem pre-delay maior | Genérico | B/I/A, `rpLevel` | 14 |
| 15 | Echo Timing / `EchoTime` | Intervalo entre repetições | Short/medium/long do perfil | Genérico; sem BPM/subdivisão | B/I/A, `etLevel` | 15 |
| 16 | Find the Distortion / `Distortion` | Saturação suave adicionada | Escolher A ou B | Genérico | B/I/A, `dsLevel` | 16 |
| 17 | High-pass cutoff / `HighPassCutoff` | Quanto grave um HPF remove | Low/medium/high do perfil | Genérico, A é referência | B/I/A, `hpfLevel` | 17 |
| 18 | Low-pass cutoff / `LowPassCutoff` | Quanto agudo um LPF preserva | Low/medium/high do perfil | Genérico, A é referência | B/I/A, `lpfLevel` | 18 |
| 19 | Low shelf / `LowShelf` | Balanço tonal amplo nos graves | Boost/cut | Genérico | B/I/A, `lsLevel` | 19 |
| 20 | High shelf / `HighShelf` | Balanço tonal amplo nos agudos | Boost/cut | Genérico | B/I/A, `hsLevel` | 20 |
| 21 | EQ gain / `EqGain` | Magnitude de boost, não volume global | Light/medium/strong do perfil | Genérico, frequência/Q fixos | B/I/A, `egLevel` | 21 |
| 22 | Compressor threshold / `CompressorThreshold` | Início relativo da compressão | Qual lado tem threshold menor | Genérico | B/I/A, `ctLevel` | 22 |
| 23 | Echo feedback / `EchoFeedback` | Persistência dos ecos | Qual lado tem feedback maior | Genérico | B/I/A, `efLevel` | 23 |
| 24 | Reverb mix / `ReverbMix` | Balanço direto/reverberado | Dry/light/medium/strong | Genérico | B/I/A, `rmLevel` | 24 |
| 25 | Find the noise / `NoiseDetection` | Ruído branco adicionado | Escolher A ou B | Genérico | B/I/A, `ndLevel` | 25 |
| 26 | Find the mix change / `MixProblem` | Diagnóstico tonal/estéreo e correção | Uma de cinco combinações problema→ação | A/B + **cinco correções audíveis C–G** + botões | B/I/A, `mpLevel` | 26 |
| 27 | EQ Matching / `EqMatching` | Recriar frequência, ganho e Q | Três parâmetros contínuos + score ponderado | **Channel strip entregue**, três knobs, curva do aluno e preview | B/I/A, `eqmLevel` | 27 |

**Referências do inventário:** CAT:8–47; GEN:27–62,104–154,197–326,343–446; EXP:24–199; LM/SP/GF; VIEW; EQ/EQJS; LOC:4921–5559.

### Particularidades que mudam a interpretação

- Level Match ainda **não** pede para mover um fader até igualar os sinais: identifica lado e, depois, um valor entre opções. [LM; VAL:170–194]
- Guess Frequency oferece quatro respostas por rodada em todos os níveis; “cinco/sete bandas” descreve o universo do perfil, não a quantidade de botões visíveis. [GEN:25,332–340; GFJS:31–40]
- Compression Ratio, cutoff, EQ gain e Echo Timing são classificações de **valores do perfil**, não estimação numérica livre. [CAT; EXP; VIEW:93–109]
- Channel Polarity não é um detector geral de fase, nem um teste de polaridade absoluta. [DSP:474–479; LOC:5289–5293]
- MixProblem já permite experimentar correções; não deve ser descrito como “só identificação”. Seu escopo ainda é um único problema controlado em uma mix completa. [EXP:179–238; VIEW:16–21]
- O score contínuo de EQ Matching é mostrado no resultado, mas o histórico principal salva `IsCorrect`, tempo e filtros; não há painel de erro por frequência/ganho/Q. [EQV; CTRL:625–640; SNAP; REPORT]
- Foundations limpa a rodada depois de validar; AudioComparison/EqMatching preservam replay cacheado. Unificar a audição do feedback é uma melhoria concreta. [LMJS:112–126; SPJS:80–92; GFJS:80–97; JS; EQJS]
- Logs opcionais preservam palpite/esperado como strings, mas o serviço de analytics pode operar em no-op ou falhar sem perder o answer; não substitui métricas garantidas no histórico. [CTRL:704–721; ANALYTICS:15–75]

## 3. Layouts mais realistas

### 3.1 Regra principal: interface de instrumento, não instrumento que responde

**Recomendação:** a interface deve transformar a hipótese do aluno em um gesto técnico.
Ela não deve analisar o estímulo visualmente e entregar o diagnóstico antes da resposta.

| Informação | Durante rodada pontuada | Após validação |
|---|---|---|
| Escolha do aluno, curva do aluno, posição de seu fader/knob | Permitida | Permitida, comparada com alvo |
| Escala fixa, valores de todas as alternativas, descrição pública do perfil | Permitida, igual para qualquer resposta | Permitida |
| Frequência, ganho, Q, pan ou threshold sorteados como alvo | **Oculta** | Pode vir em feedback do servidor |
| Analisador espectral de B, medidor L/R, correlação, waveform comparativa | Evitar: fornecem pista visual resolutiva | Úteis para explicar o fenômeno |
| GR real de cada clip, marca de cutoff, cauda desenhada com tempo real | **Oculta** | Útil, desde que medida corretamente |
| Meters “cenográficos” animados com valores fictícios | Não recomendar | Não substituir dados por ficção |

O contrato atual favorece isso: `RequestPlay` entrega tokens opacos e opções seguras, não parâmetros de processamento. [CTRL:289–335,360–369; ITA:130–190]
**Exceção existente:** `RevealAnswer` permite revelação explícita apenas em free practice; não envia o alvo automaticamente nem aceita revelação de rodada pontuada. [CTRL:843–879]
Preservar essa política explícita; nenhuma nova UI deve usar free practice para contornar a proteção de rodadas pontuadas.

### 3.2 Avaliação dos dois layouts já entregues

#### EQ Matching

- **Atual:** três ranges representados por knobs, readouts, curva SVG da hipótese do aluno e preview local do EQ sobre A. Não existe gráfico do alvo antes da validação. [EQ:20–111; EQJS:18–55,134–150]
- **Valor pedagógico:** alto; relaciona frequência, ganho e largura à ação real de ajustar um EQ, sem exigir uma DAW.
- **Já coberto no código:** ponteiro com capture, toque, Shift-drag fino, teclado nativo, `aria-valuetext` e alvo mantido no servidor. [EQJS:58–90; EQV; E2E:779–877]
- **Refinar:** entrada numérica opcional, botão de reset explícito, comparação visual pós-resposta e readouts legíveis a 360 px.
- **Tema:** a strip força `color-scheme: dark` e define paleta `--cp-*` própria; o shell pode mudar, o painel não tem variantes light/dark. [EQCSS:1–38]
- **Áudio:** preview é compensado por RMS e margem de pico; A/B canônico usa BS.1770. Não prometer equivalência perceptual exata. [ENGINE:229–305; DSP:500–534]
- **Risco adicional:** a curva usa 44.100 Hz fixos; o contexto Web Audio do preview não é fixado aqui à mesma taxa. Testar coerência em dispositivos com outras taxas. [EQJS:21–40; ENGINE:31–47,244–251]
- **Mobile:** três knobs já permanecem lado a lado; textos de limites chegam a `0.5rem`. Manter o layout existente, melhorar legibilidade antes de adicionar mais elementos. [EQCSS:205–216]

#### Boost or Cut

- **Atual:** console com transporte, dois seletores de direção e sete regiões; a resposta é composta como `direction|region`. [VIEW:24–75; JS:15–23,55–64]
- **Valor pedagógico:** alto para separar “o que aconteceu” de “onde aconteceu”; o painel não é um graphic EQ multibanda real.
- **Não repropor:** console, seletor boost/cut e sete bandas já existem; seu último ajuste também mantém os controles na região do tutorial. [VIEW:53–63; ITA:46–54]
- **Refinar:** quantidade de bandas por perfil, centros espectrais mais didáticos, contraste/tema e retorno visual pós-validação.
- **Mobile:** sete→quatro→duas colunas nos breakpoints existentes; conservar a estratégia de wrap em vez de uma mesa de sete faders minúsculos. [BCSS:137–199]
- **Limite honesto:** frequências dos botões são referências públicas das alternativas, não resposta revelada; não mover a banda correta automaticamente.

### 3.3 Propostas por exercício restante

Todos os itens abaixo são **recomendações**. A referência “atual” remete às views compartilhadas, não a um componente já implementado.

#### Foundations

**Level Match** — **Atual:** A/B e botões de lado/dB, sem medidor ou fader. [LM; LMJS] **Valor proposto:** alto se o fader for a hipótese; baixo se apenas enfeitar os botões.
- **Design:** no modo atual, seletor A/B + escala de dB clicável; em modo futuro separado, trim fader para igualar B a A e audition da escolha.
- **Ocultar:** meters reais de A/B, ganho sorteado e qualquer marca automática no fader; depois, mostrar erro residual e diferença correta.
- **360 px:** fader horizontal com botões −/+ e input numérico; não depender de arrasto vertical longo.

**Stereo Position** — **Atual:** posições em uma grade flexível; um clip, sem pan knob. [SP:8–18,46–53; SPJS] **Valor proposto:** alto; conecta percepção à operação de mixer.
- **Design:** knob/pan rail com detentes correspondentes ao perfil; fone/alto-falante esquemático indica somente o palpite.
- **Ocultar:** L/R meters e marcador automático do som; depois, desenhar palpite versus posição-alvo.
- **360 px:** rail ocupando a largura disponível, opções L/C/R ou lista de sete posições com teclado; aviso mono sempre visível.

**Guess Frequency** — **Atual:** quatro frequências em botões, geradas pelo servidor. [GF; GFJS:31–40; GEN:332–340] **Valor proposto:** alto para orientação no espectro, como escala de escolha.
- **Design:** régua logarítmica/SVG de regiões + as quatro opções numéricas; marcar somente a escolha do aluno.
- **Ocultar:** espectro de A/B, posição da banda aplicada e curva resultante; depois, curva do filtro e região correta.
- **360 px:** quatro opções em 2×2, régua simples sem 28 rótulos simultâneos; não adicionar mais opções apenas para preencher o graphic EQ.

#### Frequency & EQ

**Guess the Q** — **Atual:** três categorias, ou cinco Q no avançado; frequência varia em segredo entre três centros. [GEN:230–248; VIEW:96–107] **Valor proposto:** alto para ligar Q a largura.
- **Design:** seletor de três curvas-candidatas ou knob com cinco detentes; curvas devem representar alternativas, nunca o Q sorteado.
- **Ocultar:** curva de B; revelar sobreposição alvo/palpite após validar. Uma frequência pública fixa no iniciante evita ambiguidade sem enviar dados secretos.
- **360 px:** três miniaturas empilháveis e Q numérico; preview do palpite é evolução M, não mero CSS.

**High-pass cutoff** — **Atual:** low/medium/high no painel genérico. [EXP:100–121; LOC:5373–5387] **Valor proposto:** alto; visualiza “retirar grave” sem confundir HPF com boost/cut.
- **Design:** módulo HPF de 12 dB/oct com seletor de três cutoffs, knob detentado e curva da opção selecionada.
- **Ocultar:** cutoff de B e analisador de graves; após resposta, comparação de curvas e consequência no corpo do kick/baixo.
- **360 px:** um knob + três botões com Hz; evitar menu só com low/medium/high sem legenda.

**Low-pass cutoff** — **Atual:** classificação de três cutoffs, sem curva. [EXP:100–121; LOC:5388–5402] **Valor proposto:** alto para diferenciar extensão de agudos e loudness.
- **Design:** módulo LPF com os três valores do perfil e curva da hipótese, usando a mesma família do HPF.
- **Ocultar:** extensão espectral real de B; depois, destacar faixa removida sem sugerir que LPF é high shelf.
- **360 px:** mesma composição do HPF; botões devem acomodar a formatação fr-CA de milhares.

**Low shelf** — **Atual:** boost/cut, shelf de 150 Hz. [EXP:122–130; LOC:5403–5416] **Valor proposto:** médio/alto; diferencia platô de um bell.
- **Design:** módulo de dois estados com ícone de shelf; curva-candidata desenhada somente após seleção.
- **Ocultar:** direção de B e nível de um meter; depois, mostrar platô e a compensação global de loudness separadamente.
- **360 px:** dois botões grandes e gráfico pequeno; knob contínuo só se também mudar a dinâmica da resposta.

**High shelf** — **Atual:** boost/cut, shelf de 4.000 Hz. [EXP:122–130; LOC:5417–5430] **Valor proposto:** médio/alto, pelo mesmo motivo do Low shelf.
- **Design:** reutilizar módulo shelf; rotular brilho/ar como pistas de escuta, não como classificação universal de mix.
- **Ocultar:** curva de B; pós-resposta compara a hipótese com o platô correto.
- **360 px:** dois estados e escala simples; não criar uma strip larga só para dois botões.

**EQ gain** — **Atual:** light/medium/strong, bell em 1.000 Hz/Q 1. [EXP:131–143; LOC:5431–5445] **Valor proposto:** alto; diferencia ganho espectral de ganho global.
- **Design:** ganho com três detentes, valores públicos do perfil e bell correspondente à hipótese.
- **Ocultar:** leitura de ganho de B; depois, apresentar diferença do palpite em dB e curva correta.
- **360 px:** fader horizontal ou knob único + readout; manter os três botões como fallback acessível.

#### Dynamics

**Compression** — **Atual:** escolher o lado comprimido; nenhum GR meter. [GEN:251–267; VIEW; VAL:283–299] **Valor proposto:** médio; a strip contextualiza, o ouvido identifica.
- **Design:** transporte A/B em dois slots neutros e seletor “qual foi comprimido”; área GR marcada como disponível após resposta.
- **Ocultar:** GR, waveform de picos, luz de compressor ativo e valores que identifiquem o lado correto.
- **360 px:** dois slots empilhados; após validar, um gráfico de transiente e resumo de GR medido, não inferido só do ratio.

**Compressor Attack** — **Atual:** comparação de dois ataques com os demais parâmetros iguais. [EXP:24–31] **Valor proposto:** alto no feedback; limitado antes dele.
- **Design:** dois slots A/B, seletor de ataque mais rápido, ilustração conceitual fixa “transiente passa/é contido”.
- **Ocultar:** tempos atribuídos a A/B e envelopes reais; depois, destacar o início do ataque.
- **360 px:** desenho pós-resposta ocupa uma linha; os knobs não devem mostrar os parâmetros corretos durante o teste.

**Compressor Release** — **Atual:** escolher recuperação mais rápida, sem envelope visual. [EXP:32–39] **Valor proposto:** alto no feedback de sustain/recuperação.
- **Design:** família da strip de attack, com eixo temporal e região de recuperação exibidos após validar.
- **Ocultar:** tempos de A/B e animação de GR sincronizada com os clips durante a rodada.
- **360 px:** seleção A/B e um gráfico estático pós-resposta; preferir texto acessível a animação indispensável.

**Compression Ratio** — **Atual:** três rótulos relativos ao perfil, não knob numérico livre. [EXP:40–48; LOC:5260–5272] **Valor proposto:** alto para converter rótulos em ratios conhecidos.
- **Design:** seletor detentado dos três ratios + curva de transferência da escolha do aluno, com eixos explícitos.
- **Ocultar:** curva de transferência/GR correspondente ao alvo; após validar, comparar as duas curvas.
- **360 px:** um controle e três botões; não adicionar threshold ajustável a uma rodada que treina apenas ratio.

**Compressor threshold** — **Atual:** thresholds relativos ao pico da fonte; ratio/tempos iguais. [EXP:144–151] **Valor proposto:** médio/alto para separar threshold de volume de saída.
- **Design:** A/B e escolha “menor threshold”; após validar, escala dBFS com ambos os thresholds e exemplo medido de atuação.
- **Ocultar:** marcador de entrada e threshold de cada clip; um meter de entrada com linha de threshold resolveria a pergunta.
- **360 px:** escala horizontal pós-resposta; evitar mostrar números “universais” sem explicar a referência ao pico.

#### Stereo & Space

**Stereo Width** — **Atual:** narrow/same/wide; processamento M/S, sem width knob. [EXP:49–56; DSP:459–472] **Valor proposto:** alto para separar Side de pan.
- **Design:** knob de três estados e desenho da imagem escolhida; controles estéreo/mono para estudo futuro, não correlação automática.
- **Ocultar:** vectorscope/correlação e proporção Side real; depois, explicar multiplier, não “percentual de espaço físico”.
- **360 px:** uma escala C↔Sides, três botões e aviso de fones; não desenhar uma posição única como se fosse pan.

**Channel Polarity** — **Atual:** normal/inverted, sem botão de soma mono. [VIEW; EXP:57–62; DSP:471–479] **Valor proposto:** médio para painel de canal; alto para estudo em mono.
- **Design:** dois canais neutros e escolha de inversão; o botão Ø pertence ao palpite, nunca acende com o estado real.
- **Ocultar:** correlação e forma de onda comparativa; depois, demonstrar cancelamento mono e explicar inversão relativa.
- **360 px:** canais empilhados e botão mono de estudo explicitamente identificado; revisar acessibilidade/aviso de mudança de nível.

#### Space & Time

**Hear the Reverb** — **Atual:** detecção A/B de reverb adicionado. [EXP:63–68; LOC:5302–5315] **Valor proposto:** médio; o transporte já cumpre a dinâmica.
- **Design:** rack compacto “ambience”, seleção A/B e ilustração conceitual fixa de som direto/reflexões.
- **Ocultar:** wet meter e cauda real; após validar, mostrar wet e decay nominal.
- **360 px:** dois botões grandes; não exigir três knobs que não participam da resposta.

**Reverb Decay** — **Atual:** qual lado tem decay maior; não estima RT60 medido. [EXP:69–76; TIME:28–93] **Valor proposto:** alto após resposta, para separar duração de quantidade.
- **Design:** A/B com área de cauda pós-resposta; se houver modo de estimação futuro, knob de segundos da hipótese.
- **Ocultar:** comprimento visual das caudas reais durante a rodada; usar a palavra “nominal”, não RT60 certificado.
- **360 px:** um gráfico com texto equivalente; solicitar fontes com lacunas audíveis, não uma animação longa.

**Reverb Pre-delay** — **Atual:** comparação de dois atrasos antes da mesma rede de reverb. [EXP:77–84] **Valor proposto:** alto para um diagrama temporal pós-resposta.
- **Design:** seleção A/B e uma régua dry→gap→reflexões; os tempos sorteados só entram após validar.
- **Ocultar:** separação temporal medida de B e labels de clip com ms.
- **360 px:** régua simples e texto “gap maior”; não confundir esse knob com decay.

**Echo Timing** — **Atual:** três delays em ms, sem tempo musical. [EXP:85–93; LOC:5344–5358] **Valor proposto:** alto se o controle corresponder às alternativas.
- **Design:** knob de tempo com três detentes e readout da hipótese; pontinhos de repetições só ilustram a opção escolhida.
- **Ocultar:** eventos/timestamps reais; depois, mostrar intervalo correto e separá-lo de feedback.
- **360 px:** três botões + knob único; BPM/divisões ficam para Tempo-synced Delay, não são inferidos do áudio atual.

**Echo feedback** — **Atual:** qual lado mantém mais repetições, com delay/wet constantes. [EXP:152–159] **Valor proposto:** alto no feedback, médio na decoração anterior.
- **Design:** rack da família echo; seletor A/B e sequência de repetições pós-resposta.
- **Ocultar:** número/amplitude de repetições reais durante o teste; a primeira repetição não deve ser usada como explicação de feedback.
- **360 px:** duas escolhas e mini gráfico; botões de transporte mantêm wrap.

**Reverb mix** — **Atual:** quatro quantidades relativas, com opção dry. [EXP:160–172] **Valor proposto:** alto; blend é ação de aux/insert, diferente de decay.
- **Design:** blend de quatro detentes, legendas das quatro alternativas e desenho do equilíbrio escolhido.
- **Ocultar:** wet real e meter de send/return; depois, comparar o blend escolhido com o correto.
- **360 px:** controle horizontal + 2×2 opções; explicar que wet é coeficiente de mistura, não loudness percebido.

#### Critical Listening / diagnóstico

**Find the Distortion** — **Atual:** detectar saturação `tanh`, não clipping duro. [EXP:94–99; DSP:479–486] **Valor proposto:** médio antes, alto no feedback de harmônicos/não linearidade.
- **Design:** rack compacto de textura, A/B e resposta; curva de transferência/harmônicos apenas no feedback.
- **Ocultar:** LED de clipping, THD ou waveform achatada que identifique o clip.
- **360 px:** conservar dois botões; modelos tube/tape não podem ser insinuados por ilustrações de equipamentos ausentes.

**Find the noise** — **Atual:** ruído branco acrescentado em três SNRs; não classificação de hum/buzz. [EXP:173–178; DSP:421–437] **Valor proposto:** baixo para decoração, alto para noise floor.
- **Design:** A/B simples, “escute os intervalos silenciosos”, com spectrum/noise floor só após validar.
- **Ocultar:** SNR, ruído visualizado e LED de detector; depois, explicar SNR RMS.
- **360 px:** priorizar reprodução e instrução; um knob que não muda a resposta adicionaria carga sem benefício.

**Find the mix change / MixProblem** — **Atual:** problema→correção em um botão; cinco correções C–G audíveis antes da escolha. [VIEW:16–21,95–100; EXP:208–215; AB:35–53] **Valor proposto:** alto para workflow, não para uma DAW completa.
- **Design:** “diagnosticar → escolher ação → experimentar → validar”, em cards de channel strip; cada card indica sua própria hipótese.
- **Ocultar:** destaque de “correção correta”, valores secretos do problema e diagnóstico automático; as ações-candidatas podem ser descritas publicamente.
- **360 px:** uma ação por card; A/B em cima, cinco propostas abaixo; considerar carregar C–G sob demanda com mudança explícita no contrato de readiness.

### 3.4 Prioridade/esforço de layout

| Exercício | Ganho pedagógico de realismo | Prioridade | Esforço | Primeiro incremento |
|---|---|---|---|---|
| Level Match | Alto com resposta por fader | P1 | M | Escala de hipótese; modo contínuo separado |
| Stereo Position | Alto | P1 | S–M | Rail/pan detentado |
| Guess Frequency | Alto | P1 | S–M | Régua logarítmica de escolha |
| Boost or Cut | **Já entregue** | P1 | S–M | Refinar perfil, temas e leitura |
| Guess the Q | Alto | P1 | M | Curvas-candidatas e detentes |
| Compression | Médio | P2 | M | Strip neutra + feedback GR |
| Compressor Attack | Alto no feedback | P1 | M | Diagrama de transiente pós-resposta |
| Compressor Release | Alto no feedback | P1 | M | Recuperação pós-resposta |
| Compression Ratio | Alto | P1 | M | Detentes + curva do palpite |
| Stereo Width | Alto | P1 | S–M | Controle de width da hipótese |
| Channel Polarity | Médio; alto em estudo mono | P2 | M | Painel Ø + demonstração após resposta |
| Hear the Reverb | Médio | P2 | S | Rack compacto, sem knobs inúteis |
| Reverb Decay | Alto no feedback | P2 | M | Cauda pós-resposta |
| Reverb Pre-delay | Alto no feedback | P2 | M | Gap pós-resposta |
| Echo Timing | Alto | P2 | S–M | Tempo detentado |
| Find the Distortion | Médio | P2 | S–M | Explicação de saturação |
| High-pass cutoff | Alto | P1 | S–M | Módulo HPF compartilhado |
| Low-pass cutoff | Alto | P1 | S–M | Mesmo módulo, LPF |
| Low shelf | Médio/alto | P1 | S | Duas curvas-candidatas |
| High shelf | Médio/alto | P1 | S | Reuso do módulo shelf |
| EQ gain | Alto | P1 | S–M | Controle de ganho espectral |
| Compressor threshold | Médio/alto | P2 | M | Escala explicativa pós-resposta |
| Echo feedback | Alto no feedback | P2 | M | Persistência das repetições |
| Reverb mix | Alto | P1 | S–M | Blend com detentes |
| Find the noise | Baixo para decoração | P3 | S | Manter simples; melhorar feedback |
| MixProblem | Alto | P1 | M | Fluxo de diagnóstico/ação e mobile |
| EQ Matching | **Já entregue** | P1 | M | Refinar scoring, preview, temas e feedback |

### 3.5 Componentes compartilhados propostos

| Componente | Base existente | Proposta e limite |
|---|---|---|
| Knob acessível | EQ/EQJS | Extrair range + pointer interaction; web component opcional, sem exigir framework novo. |
| Rail/fader | Ranges existentes | Palpite com texto/input numérico e −/+; reservar headroom no preview. Em LevelMatch, não usar compensação RMS/LUFS que anule a mudança de ganho. |
| Curva EQ SVG | EQJS:20–40 | Separar renderização da resposta; modos `candidate`, `learner`, `feedback`, nunca `hidden-target` antes de validar. |
| Rack de processo | VIEW + strip atual | Slot neutro comum para A/B; cada exercício conserva sua dinâmica. |
| Transporte | AB/ABJS/ENGINE | Reutilizar preload, alternância sincronizada, replay e atalhos; não criar player por exercício. |
| Meters/GR | Ainda não existem nas UIs de Áudio | Começar no feedback; estatísticas reais de GR exigem instrumentação do DSP, não só um widget. |
| Feedback técnico | `detail` dos validadores | Template comum: palpite, correto, diferença, pista de escuta, replay, próximo passo. |
| Tokens de tema | CSS:42–145 | Mapear consoles a `--aa-*` ou variantes explícitas; não copiar paletas independentes para cada novo módulo. |

### 3.6 Requisitos transversais de aceite

- **360 px primeiro:** uma/duas colunas empilháveis; `min-width:0`; SVG com `viewBox`; nenhuma mesa com largura mínima fixa.
- Em `<main>`, usar gutters **no máximo `gx-4` abaixo de `xl`**; `g-4` atual atende ao limite. [INDEX:83,111; E2E:2054–2059]
- Touch targets propostos ≥44×44 CSS px; foco visível, rótulos completos e feedback sem depender só de cor.
- Ranges continuam com teclado nativo; se um controle virar `slider` customizado, implementar setas/Home/End e semântica equivalente.
- Drag fino é complemento, não requisito; a pessoa precisa conseguir responder sem mouse e sem gesto preciso.
- Não anunciar a cada frame de meter; usar resumo textual e respeitar `prefers-reduced-motion`. [CSS:3656–3667]
- Traduzir labels, ajuda, estados, unidades formatadas e dicas em **en-US / pt-BR / fr-CA**; serialização dos números continua invariant.
- Manter `data-tour="answer"` e as referências do tutorial; testes percorrem **todas** as páginas seedadas. [TUTOR:61–77]
- Testar resposta incompleta, troca de nível, reset, erro de carregamento, replay pós-validação e ausência de dados-alvo em DOM/JSON.
- Estéreo no celular é requisito de saída, não de viewport: uma tela de 360 px não torna o alto-falante mono apropriado para pan/width/polarity.

## 4. Comparação com o README-Audio-Ear-Training.md

### 4.1 Critério de rastreabilidade

**Fonte:** `C:\Users\lramoscostah\Downloads\README-Audio-Ear-Training.md`, lido integralmente; linhas da matriz referem-se a ele, e evidência à implementação.

- **Implementado:** a habilidade/dinâmica existe, mesmo com identificador diferente.
- **Parcial:** só parte da habilidade, resposta ou variedade foi entregue.
- **Ausente:** não aparece no catálogo/geradores examinados; isso não é prova sobre branches/PRs ainda não incorporados.
- **Diferente deliberadamente:** existe uma escolha explícita de escopo/arquitetura; não é um defeito automaticamente.

### 4.2 Matriz — MVP, exercícios e variações

| Ideia do README / linhas | Situação | Correspondência atual e limite | Evidência |
|---|---|---|---|
| Ouvir e decidir tecnicamente em produção, 23–37 | Parcial | Reconhecimento está amplo; EQMatching/MixProblem iniciam a ação técnica, sem workflow completo de mix | CAT; EQ; EXP:179–238 |
| Dois pilares Music/Audio e terceira camada Mix, 5–20,728–758 | Parcial | Music/Audio visíveis; `Mix` existe vazio, sem unidades/experiência completa | TRACK:17–27; PATH:87–105; CTRL:101–120 |
| Seis áreas, 41–61 | Implementado nos dados | Seis categorias de Áudio; falta navegação por categoria no catálogo | TRACK:24–26; SEED:78–83; INDEX |
| Guess Frequency: boost e múltipla escolha, 69–86 | Implementado | A/B loudness-matched; quatro opções; +9/+6/+6 dB, não um único exemplo +6/Q1.4 | GEN:49–62,104–154 |
| Guess Frequency B: 100/500/1k/5k/10k, 90–96 | Implementado | Mesma lista de cinco centros | GEN:53 |
| Guess Frequency I: 125/250/500/1k/2k/4k/8k, 98–107 | Implementado | Mesma lista de sete centros | GEN:54 |
| Guess Frequency A: ISO desde 63/80/100…500, 109–118 | Parcial | Sete bandas adjacentes apenas de 500 a 2.000 Hz, por qualificação de fontes | GEN:55,157–169; DOC:530–555 |
| Boost or Cut: referência/alterado, direção + região, 122–144 | Implementado | Sete regiões, 14 combinações; dois seletores, não 14 botões | CAT:40–44; GEN:197–219; VIEW:53–75 |
| Guess the Q: frequência/ganho conhecidos, 147–158 | Parcial / diferente | Ganho +9 fixo no código; frequência varia entre três centros e só é informada no feedback | GEN:230–248; LOC:5191–5195; JS:110–113 |
| Guess the Q A: 0.5/1/2/4/8, 160–165 | Implementado | Cinco alternativas numéricas, não resposta contínua | GEN:236–241; VIEW:102–107 |
| Level Match: lado + diferença, 170–179 | Implementado | B só lado; I/A exigem lado e diferença exata entre opções | GEN:27–33,423–434; VAL:170–194 |
| Level Match B/I/A e seus valores, 180–197 | Implementado | Mesmos conjuntos 6/9/12; 2/3/4/6; 0.5/1/1.5/2 | GEN:30–32 |
| Compression: qual lado foi comprimido, 201–214 | Implementado | Lado aleatório e loudness equivalente | GEN:251–267,305–326 |
| Compression leve/moderada/pesada, 216–222 | Parcial | Perfis de detecção e classificação de ratio; não comparação geral de modelos/GR | GEN:305–311; EXP:40–48 |
| Compressor vs limiter e compressão paralela, 222–223 | Ausente | Compressor atual não tem limiter/lookahead; paralelo não está no gerador | DSP:439–458; DSPPLAN |
| Attack & Release, 226–244 | Implementado como dois exercícios | `CompressorAttack` e `CompressorRelease`; não uma única página | CAT:10–11; EXP:24–39 |
| Attack/release em caixa/kick/baixo/vocal/guitarra/piano/mix bus, 236–244 | Parcial | Bundled usa full-mix; storage usa subset musical de compressão, sem seletor desses stems | EXP:242–250; SOURCES; LIB:243–248 |
| Stereo Position sete posições, 247–262 | Implementado com progressão própria | B L/C/R; I cinco; A sete, incluindo os L/R25/50/75 do exemplo | GEN:35–47 |
| Pan: posições intermediárias mais finas, 264–267 | Parcial | Sete detentes no A; não estimação contínua | GEN:43–46; VAL:202–216 |
| Pan: múltiplas fontes, width e mid/side, 268–270 | Parcial | Width M/S é outro exercício; múltiplas fontes localizadas não existem | DSP:461–472; CAT |
| Reverb: dry/pouco/muito, 273–282 | Implementado com dinâmica diferente | Presence é detecção; ReverbMix tem dry + três quantidades | EXP:63–68,160–172 |
| Room/Plate/Hall/Chamber, 284–289 | Ausente | Uma rede algorítmica, não classificação de tipos | TIME:28–96; DSPPLAN |
| Decay e pre-delay, 291–295 | Implementado parcialmente | Comparação de dois valores; sem estimação contínua/número livre | EXP:69–84; EV |
| Early reflections e tamanho aparente do ambiente, 296–297 | Ausente | Não há parâmetros/exercícios próprios para esses objetivos | CAT; DSPPLAN; TIME |
| Find the Mix Problem, 299–321 | Parcial | Cinco problemas EQ/width + correções audíveis; não todo o conjunto do README | EXP:179–238; VIEW:16–21 |
| Excesso de 250 Hz, vocal agressivo em 3 kHz, mix escura, 309–311 | Parcial | Muddy em 250; harsh em 4k na mix, sem vocal isolado; “mix escura” não é opção | EXP:184–190; LOC:5513–5517 |
| Excesso de compressão, kick mascarando baixo, 312–313 | Ausente como diagnóstico | Detectores individuais existem; MixProblem não gera esses problemas | EXP:179–199 |
| Estéreo excessivamente aberto, fase, reverb excessivo, clipping, 314–317 | Ausente como diagnóstico | MixProblem contém apenas imagem **estreita**, não essas quatro opções | CAT:29; EXP:184–191 |
| Find the Boost / Find the Cut, 327–328 | Parcial / não separado | Objetivo coberto por BoostOrCut; modo só boost/cut não existe | CAT; GEN:197–219 |
| How Much EQ?, 330 | Implementado parcialmente | EQ gain classifica três boosts em 1k/Q1; não magnitude contínua/cuts | EXP:131–143 |
| Low / Mid / High, 332 | Parcial / não separado | Regiões em GuessFrequency/BoostOrCut, sem exercício dedicado de três bandas | CAT; GEN:132–139,201–205 |
| Frequency Sweep Hunt, 333 | Ausente como dinâmica | Preview ajustável de EQ Matching não equivale a busca por sweep com objetivo próprio | CAT; EQJS:134–150 |
| Which Mix is Brighter?, 334 | Parcial / não separado | High shelf treina direção tonal; não classificação direta “qual é mais brilhante” | EXP:122–130; LOC:5375–5388 |
| Which Mix is Warmer?, 335 | Parcial / não separado | Low shelf é proxy útil, sem essa pergunta/variedade | EXP:122–130; LOC:5361–5374 |
| Masking, 336 | Ausente | Nenhum gerador compara competição entre stems | CAT; DSPPLAN |
| EQ Matching: A/B + recriar F/G/Q, 340–382 | Implementado | Controles contínuos, audition local, alvo privado e resultado numérico | GEN:270–302; EQ; EQJS; EQV |
| Compression or No Compression?, 389 | Implementado | `Compression` cobre detecção de processamento adicionado | GEN:251–267 |
| Guess the Ratio, 390 | Parcial / diferente | `CompressionRatio` classifica light/medium/strong do perfil | EXP:40–48; LOC:5260–5271 |
| Attack Time / Release Time, 391–392 | Implementado parcialmente | Comparação de rapidez, não estimativa em ms | EXP:24–39 |
| Threshold Challenge, 393 | Implementado parcialmente | Comparação de threshold relativo ao pico, não ajuste/estimação | EXP:144–151 |
| How Much Gain Reduction?, 394 | Ausente | GR não é resposta nem detalhe medido nos validadores atuais | DSP:439–458; VAL:283–299; EV |
| Transient Detective, 395 | Parcial / não separado | Attack treina parte do efeito; sem transientes editados como tarefa independente | EXP:24–31; CAT |
| Limiter or Compressor?, 396 | Ausente | Não há limiter como processor ou exercício | DSPPLAN; CAT |
| Peak vs RMS, 397 | Ausente | Métricas internas não equivalem a treino com essa pergunta | CAT; DSPPLAN |
| Loudness Guess, 398 | Parcial / não separado | LevelMatch é ganho relativo, não palpite LUFS ou loudness absoluto | GEN:383–446 |
| Clean or Saturated?, 404 | Implementado com outro nome | `Distortion` detecta saturação suave acrescentada | EXP:94–99; DSP:482–487 |
| Saturation Amount, 405 | Parcial / não separado | Níveis mudam a knee, mas resposta continua A/B; não estima intensidade | EXP:94–99; CAT:19 |
| Tube / Tape / Clean, 406 | Ausente | `tanh` único; nenhum modelo/model identification | DSP:482–487; DSPPLAN |
| Clipping Detective, 407 | Ausente | Saturação suave não representa clipping duro/digital | DSP:482–487; CAT |
| Pan Position / Stereo Width, 413–414 | Implementado | StereoPosition e StereoWidth | GEN:343–380; EXP:49–56 |
| Mono Compatibility, 415 | Ausente | Inversão de um canal é apenas um caso; não há tarefa de compatibilidade mono | CAT; VIEW; DSP:474–479 |
| Polarity Flip, 416 | Implementado com escopo explícito | Inversão **relativa** de canal, não polaridade absoluta | EXP:57–62; LOC:5289–5293 |
| Phase Problems, 417 | Parcial / não separado | Só a inversão de polaridade; sem atraso/interferência geral | DSPPLAN; CAT |
| Which Mic Is Closer?, 418 | Ausente | Sem simulação/dataset de distância de microfonação | CAT; DSPPLAN |
| Reverb or Dry?, 424 | Implementado com ressalva | “Original” pode já ter ambience; pergunta é sobre reverb **adicionado** | EXP:63–68; LOC:5304 |
| Guess Reverb Time / Pre-delay, 425–426 | Parcial | Compare valores, sem resposta numérica livre | EXP:69–84 |
| Room / Plate / Hall, 427 | Ausente | Sem modelos/tipos de reverb | TIME; DSPPLAN |
| Delay Time, 428 | Implementado parcialmente | EchoTime classifica três intervalos em ms | EXP:85–93 |
| Tempo-synced Delay, 429–436 | Ausente | Não há BPM nem 1/4, 1/8, dotted 1/8, 1/16 | LOC:5348; EXP:85–93 |
| Noise Detective: hiss, 441,447–454 | Parcial | Detecta ruído branco acrescentado; não classifica tipo | EXP:173–178; DSP:421–437 |
| Hum, buzz, ground loop, fan, electrical noise, 450–454 | Ausente | Ruído branco não cobre esses mecanismos | DSPPLAN; CAT |
| Artifact Hunt: clicks/pops/clipping/codec/aliasing/digital distortion, 442,456–463 | Ausente | Nenhuma rodada específica para esses artefatos | CAT; DSPPLAN |
| Which Mix is Better Balanced?, 443 | Ausente como julgamento global | MixProblem aplica um defeito controlado; “melhor” precisa de critério musical | EXP:179–199 |
| Which Mix is Clearer?, 444 | Ausente | Sem tarefa de clareza/masking geral | CAT |
| Find the Problem, 445 | Parcial | Alias conceitual de MixProblem, limitado a cinco problemas | CAT:29; EXP:179–238 |
| Decidir ação e ouvir resultado de cada opção, 467–490 | Parcial, com parte importante entregue | MixProblem já oferece cinco reparos; não ações contínuas/stems/cadeia livre | EXP:208–238; AB:35–53 |
| Muddy Vocal: cortar 250/boost 80/reverb/comprimir 10:1, 500–508 | Parcial | Correção de low mids em mix completa; não cenário vocal nem os quatro distractors | EXP:184–191,230–238 |
| Harsh Vocal: upper mids/sub/width/decay, 510–518 | Parcial | Harsh em 4k na mix e cut correspondente; sem vocal/stem ou ações variadas | EXP:184–191,230–238 |
| Kick vs Bass: EQ complementar/sidechain/envelope/width, 520–528 | Ausente | Não há stems concorrentes, sidechain ou escolha dessas soluções | DSPPLAN; CAT |
| Flat Drums: ataque/limiting/transientes, 530–537 | Ausente como cenário | Attack existe isoladamente; limiter/transient shaping não | EXP:24–31; DSPPLAN |
| Washed-out Mix: reverb/decay/pre-delay/low-mid, 539–547 | Ausente como cenário | Treinos de reverb existem, mas o diagnóstico combinado não | EXP:63–84,179–199 |
| Mix Challenges de vocal/drums/bass/guitar/mix bus/full mix, 887–895 | Parcial | Full mix com cinco defeitos; demais cenários e workflow não entregues | EXP:179–238; TRACK:26; DOC:104–106 |

### 4.3 Matriz — níveis, dinâmica, áudio, pontuação e engajamento

| Ideia / linhas do README | Situação | Estado verificado / lacuna | Evidência |
|---|---|---|---|
| Beginner: diferenças grandes/poucas opções, 555–560 | Parcial | Magnitudes maiores em muitos exercícios; número de opções quase sempre não diminui | GEN; EXP; CAT |
| Intermediate: diferenças menores/mais opções, 561–566 | Parcial | LM/SP aumentam resposta; outros mantêm categorias/quantidade | GEN:27–62; CAT |
| Advanced: sutileza, mais variáveis/material complexo, 567–572 | Parcial | Sutis em DSP; fontes da expansão em geral são as mesmas, exceto polarity | EXP:242–250; LIB:255–268 |
| Pink noise/white noise, 582–583 | Parcial | Pink noise é fonte; white noise é processor adicionado, não fonte separada | SOURCES:3–33; DSPPLAN:44–47 |
| Vocal/speech, 584–585 | Não verificado como fonte dedicada | Catálogo bundled não contém esses assets; storage rotula tudo como `mix` | SOURCES; LIB:208–227 |
| Kick/snare/overheads/percussion, 586–588,595 | Parcial | Drum-loop com kick/snare/hihat; sem catálogo separado de overheads/stems | SOURCES:35–72 |
| Bass/synth/full mix, 589,594,596 | Implementado como fontes | Bass-line, synth-chords, full-mix e eq-reference-mix | SOURCES:68–209 |
| Acoustic/electric guitar/piano, 590–592 | Ausente como fontes técnicas dedicadas bundled | Instrumentos musicais da outra track não são automaticamente os clips técnicos | SOURCES; GEN:157–169 |
| Generalização entre fontes, 599–603 | Parcial | Várias fontes elegíveis, preferência por músicas privadas; pouca escolha/contexto de tipo no produto | GEN:171–177; EXP:242–250 |
| Multiple choice: acerto, 607–612 | Implementado | Correto/erro, `BestScore` e XP | SCORE; XP |
| Multiple choice: tempo como pontuação, 610 | Diferente deliberadamente | Tempo é medido no servidor e reportado, não multiplicador do score/XP de Áudio | CLOCK; CTRL:608–640; XP:5–7 |
| Multiple choice: streak como pontuação, 611 | Parcial / diferente | Streak de prática existe; não há bônus de acertos consecutivos neste scoring | XP:43–79; SCORE |
| Multiple choice: dificuldade como pontuação, 612 | Ausente como multiplicador | 10 XP/acerto e 2/erro, independentemente do perfil | XP:5–7; CTRL:722–727 |
| Continuous score F/G/Q, 614–631 | Implementado | Pesos 50/30/20, tolerâncias e critério >80% | EQV:8–36 |
| Exemplo score 91%, 359–371,627–631 | Diferente pela tolerância adotada | Alvo2500/+5/Q1.4 versus palpite2700/+4.6/Q1.2 recebe **100%** pela fórmula atual;91% no README é ilustrativo, não algoritmo normativo | EQV:32–40; DESIGN:359–371 |
| Feedback “you chose/correct/tip”, 635–663 | Parcial | Valores corretos/dicas existem; maioria não repete o palpite nem explica especificamente o erro | GFJS:83–96; JS:104–120; LOC |
| Analytics por Frequency/EQ/Dynamics/Stereo/Reverb/Critical, 666–679 | Parcial | `ByCategory` já existe; Frequency/EQ estão juntos e radar usa um único tipo AudioComparison | REPORT:13,42–47; DASH:284–291 |
| Frequências/regiões mais erradas, 681–683 | Parcial na coleta, ausente no painel | GF/BC guardam metadados; relatório de dificuldades pessoais agrega por exercício | GEN:149–153,215–219; REPORT:61–65 |
| Tipos de compressor mais confundidos, 684 | Ausente | Nem tipos de compressor nem matriz de confusão no dashboard | DSPPLAN; REPORT |
| Evolução/accuracy/tempo/streak/sessões, 685–690 | Implementado | Timeline, histórico, KPIs, dificuldade jogada e streak global | DCTRL:60–101; REPORT; DASH |
| Recomendação low-mid 200–500, 691–700 | Parcial / ausente no detalhe | Recomenda revisar exercício; não banda específica | REPORT:64–74; DSTATS:158–182 |
| Recomendação Q Intermediate, 703–709 | Ausente no detalhe | Não analisa score de Q nem sugere filtro/nível específico por parâmetro | EQV; REPORT; SNAP |
| Golden Ears/Frequency Hunter/EQ Master, 711–721 | Parcial, com outros nomes | AudioAnalyticalEar cobre desempenho geral; AudioFrequencyRadar cobre GF; matching de EQ ainda não tem badge próprio | BADGE; BRULE:83–91 |
| Dynamic Control/Stereo Vision/Phase Detective, 722–724 | Parcial, com outros nomes | AudioStereoPosition cobre pan; dynamics/polarity ainda sem conquistas próprias | BADGE; BRULE:83–91 |
| Reverb Explorer/Mix Doctor/Critical Listener, 725–727 | Parcial | Desempenho/exploração geral já têm badges Audio; domínio específico de reverb/diagnóstico ainda não | BADGE; BRULE:83–91; ART |
| Fluxo exercício→nível→ouvir→responder→feedback→próxima, 761–771 | Implementado | Views, filtros, practice e validação reutilizados | FVIEW; LMJS; JS; EQJS |
| Replay ilimitado da mesma rodada, 776–777 | Implementado dentro do ciclo/token | Replay não gera pergunta; token tem TTL e pode expirar | ABJS:124–125; TOKEN:16–18 |
| Alternância A/B instantânea, 779–789 | Implementado após preload | Dois buffers iniciam juntos; troca por ganho com transição curta | ENGINE:123–193; ABJS:43–76 |
| Gain matching, 791–795 | Implementado com exceções corretas | BS.1770 nos comparativos; LM altera nível; pan usa potência constante; preview EQ usa RMS | GEN; DSP:500–534; ENGINE:273–305 |
| Randomização de posição das respostas, 797–798 | Parcial | GF sorteia conjunto/posição; outras alternativas mantêm ordem fixa; A/B de detecção é aleatório | GEN:332–340; EXP:200–205; VIEW:95–107 |
| Blind testing, 800–802 | Implementado com semântica apropriada | Detecção A/B cega; quando pergunta é parâmetro, A referência/B alterado é explícito e não revela esse parâmetro | CAT:3–4; GEN; CTRL:360–369 |
| Evitar clipping acidental/processamento consistente, 805–809 | Implementado no contrato | Limites/guardas, planos determinísticos e versão no hash | DSP:17–45,49–72,303–326; DOC:340–417 |
| Loudness/material sem pistas indevidas, 810–814 | Parcial, requer escuta | Guardas/qualificação numérica existem; fonte de BC bundled depende da frequência; revisão humana não foi feita aqui | GEN:221–228; PRIVATE; DOC:1033–1044 |
| Lossless/codec transparente, 815 | Implementado como formato do pipeline | Fontes PCM WAV e render processado WAV; nenhuma conclusão sobre hardware do usuário | SOURCES; DOC:257–266,354–355 |
| 28 frequências ISO, 819–854 | Parcial | Não há conjunto discreto completo; GF A usa sete bandas. EQ Matching contínuo não substitui reconhecimento de todas | GEN:49–62,270–302 |
| Phase 1: nove exercícios, 860–872 | Diferente deliberadamente, temas cobertos | Primeiro lançamento foi trio Foundations; expansão passou a cobrir os demais temas | DOC:53–92; CAT |
| Phase 2: EQ/ratio/GR/width/phase/reverb/delay/noise/artifacts, 874–885 | Parcial | EQ/ratio/width/reverb/delay/noise entregues; GR, fase geral e artifacts continuam lacunas | CAT; DSPPLAN |
| Phase 3: cenários interativos de mix, 887–895 | Parcial | Reparos discretos entregues; sem stems/controles contínuos/track Mix populada | EXP:208–238; TRACK |
| Phase 4: sessão adaptativa 5×low-mid/5×Q/5×attack/3×mix, 897–910 | Ausente | Há recomendações por exercício, não session builder técnico adaptativo | REPORT:64–74; GAMES:38–43 |
| Ouvir→diagnosticar→agir, 914–945 | Parcial, direção correta | EQ Matching e MixProblem aproximam ação; a maioria permanece classificatória | EQ; VIEW; EXP |

#### Badges de Áudio incorporados durante a revisão

| Chave atual | Condição verificada | Relação com a proposta do usuário |
|---|---|---|
| `audio_first_contact` | Uma sessão Audio com ≥5 respostas; gaps entre respostas ≤30 min | Entrada/engajamento; não basta uma questão |
| `audio_level_match` | 10 respostas corretas em LevelMatch | Ganho global; não mastery avançado |
| `audio_stereo_position` | 20 respostas corretas em StereoPosition | Pan; não width/polarity |
| `audio_frequency_radar` | 20 respostas corretas em GuessFrequency | Reconhecimento de frequência |
| `audio_analytical_ear` | 3 sessões Audio, cada uma ≥10 respostas e ≥80% corretas | Desempenho geral, não erro por parâmetro |
| `audio_explorer` | Ao menos uma resposta em cada exercício conhecido de Audio | Cobertura; não exige acerto |
| `audio_foundations_mastered` | Conclusão cronológica dos três passos Foundations | Critério7/10 da trilha; não exige filtro beginner |

As sete definições estão disponíveis, com sete WebP em `AcademiaAuditiva\wwwroot\img\badges` e textos nas três culturas; isso não comprova unlock em produção. Não criar aliases redundantes. [BADGE:84–114; BRULE:83–91,173–187; SESSIONS:21–49; SEED:1775–1803; LOC:3859–3900]

### 4.4 O que acrescentar ou melhorar, sem duplicação de propósito

| Item concreto | Prioridade / esforço | Entrega mínima útil | O que não fazer |
|---|---|---|---|
| Feedback palpite→correto→pista→replay | P1 / M | Templates comuns e visual apenas após validar | Novos textos genéricos iguais para todos os erros |
| Level Match contínuo | P2 / M | Modo opcional de trim, erro residual em dB, preview da escolha | Renomear os botões existentes como se já fosse matching |
| Matching de cutoff/ratio/tempo | P2 / M–L | Um parâmetro por vez, tolerância declarada e audition | Criar três novos nomes para a mesma rodada classificatória |
| Mono Compatibility | P2 / M–L | Referência estéreo/mono, perda audível e diagnóstico com critério objetivo | Tratar polarity inversion como todas as falhas de mono |
| Masking / Kick vs Bass | P2 / L | Dois stems licenciados, problema controlado, correção complementar | Aplicar EQ em full-mix e alegar treino de competição entre fontes |
| Clipping / clicks / hum | P2 / M–L | Separar três fenômenos, DSP/dataset determinístico e guardas de nível | Chamar `tanh` de clipping digital ou white noise de ground loop |
| Mix Challenges por cenário | P2 / L | Primeiro Muddy Vocal com stem e quatro ações audíveis | Construir browser DAW inteira antes de validar uma situação |
| Tipos de reverb e compressor/limiter | P3 / L | Modelos identificáveis, fontes apropriadas e revisão auditiva | Trocar ícones sem implementar os processos diferentes |
| Materiais dedicados e generalização | P1 / L | Speech/vocal/drums/bass com licença, tags e qualificação por tarefa | Usar gravações arbitrárias ou repetir fontes que já têm o defeito dominante |
| Analytics por banda/parâmetro | P2 / M | Métricas derivadas **após** validação, perfil versionado e painel simples | Enviar alvo em RequestPlay para que o navegador calcule score |
| Sessões adaptativas de Áudio | P3 / L | Recomendação por filtro, depois playlist curta com critérios de progressão | Liberar weak-spots de Music para Audio sem revisar elegibilidade |
| Badges de Áudio adicionais | P3 / M–L | Complementar os sete atuais com EQ/dynamics/reverb, somente após critérios de domínio | Duplicar Frequency Radar/Panorama Master com aliases ou ampliar badges legados de Music |

**ISO completo:** o README lista 31.5/40/50/63/80/100/125/160/200/250/315/400/500/630/800/1k/1.25k/1.6k/2k/2.5k/3.15k/4k/5k/6.3k/8k/10k/12.5k/16k Hz.
Recomenda-se ampliar por **janelas espectrais qualificadas**, não liberar as 28 bandas para qualquer fonte.

### 4.5 Ajustes na documentação interna já existente

- `docs\Audio-Ear-Training.md:864` ainda registra Boost or Cut como três centros/“six answers”; hoje são sete centros/14 combinações. [CAT:40–44; GEN:201–205]
- `docs\Audio-Ear-Training.md:865` ainda informa Q avançado 0.7/1.4/2.8; hoje são cinco alternativas 0.5/1/2/4/8. [GEN:235–241; LOC:5201]
- O bloco “Current architecture and gaps” preserva lacunas iniciais como se todas ainda fossem atuais; contrastá-lo com as subseções “as built”. [DOC:155–176,197–214]
- A descrição de engagement cita apenas Foundations/`lmLevel`/`spLevel`/`gfLevel`; a implementação atual abrange também a expansão por catálogo. [DOC:831–848; DRULE; REPORT:120–137]
- A regra de adicionar exercício em Backlog foi escrita para Music: novos Áudio entram no gerador técnico antes de `ExercisePlaybackPlanner`. [BACKLOG:218–242; CTRL:252–255]
- Ao atualizar o status de gamificação, registrar os sete badges Audio já incorporados; não descrever seu escopo como somente XP/streak ou como inteiramente futuro. [BADGE; BRULE]
- Não duplicar o roadmap de Mix Challenges/adaptação como se fosse descoberta inédita: já está registrado em DOC:104–106,122–128.
- **Este relatório não altera esses documentos:** sugere uma PR curta de sincronização, preservando o histórico de slices.

## 5. Categorias de exercícios

### 5.1 Diagnóstico do modelo atual

| Conceito | Implementação atual | Implicação para a proposta |
|---|---|---|
| Track | Derivada de `ExerciseCategory.Name` por `TrainingTracks` | Não precisa de `Exercise.TrackId` agora. |
| Category | FK `Exercise.ExerciseCategoryId`; lookup com Name/DisplayName | Já comporta as seis áreas; não criar um segundo campo paralelo. |
| Type | FK `Exercise.ExerciseTypeId`; todos os Áudio são `AudioComparison` | Tipo não organiza Frequency/Dynamics/Reverb nem distingue resposta contínua. |
| Difficulty seed | FK única; todos os Áudio entram como Beginner | É padrão de entrada, não lista de níveis suportados. |
| Perfis | `FiltersJson` por exercício e geradores no servidor | B/I/A deve continuar vindo do filtro declarado. |
| Catálogo | `ByCategory`/`ByTrack` code-first; página `/Exercise` usa banco | A categoria existe, mas a página oferece apenas abas nível/tipo. |
| Dashboard | `ByCategory` já agrega categorias; radar agrega tipos | Categoria distingue habilidades de Áudio melhor que o único tipo AudioComparison. |
| DailyChallenge | Escolhe três exercícios, preferindo categorias distintas | Recategorizar muda sorteio diário e distribuição de prática. |

**Evidências:** MODEL; SEED:39–59,71–105,1590–1732; TRACK:23–64; CATALOG:13–54; INDEX:57–136; REPORT:42–47; DRULE:44–63,143–162.

**Correção da premissa:** não faltam “proper Audio categories” no modelo; falta **torná-las encontráveis na página de exercícios**.
O catálogo hoje oferece duas formas de agrupamento, não três filtros combináveis. [INDEX:58–72]

### 5.2 Taxonomia recomendada: manter as seis chaves

| Chave existente | Nome de área recomendado | Quantidade | Conteúdo / fronteira |
|---|---|---:|---|
| `Level` | Nível e ganho | 1 | Ganho global; não EQ gain nem loudness absoluto. |
| `FrequencyEq` | Frequência e EQ | 9 | Centro, largura, ganho espectral, filtros e matching de EQ. |
| `Dynamics` | Dinâmica | 5 | Detecção, ratio, threshold, attack/release. |
| `StereoPhase` | Estéreo e polaridade | 3 | Pan/width/inversão; ampliar “fase” quando existir treino geral. |
| `SpaceTime` | Espaço e tempo | 6 | Reverb/echo: presença, quantidade, duração e timing. |
| `CriticalListening` | Escuta crítica e diagnóstico | 3 | Saturação, ruído e mix problem; futuro cleanup/artefatos. |

**Decisão de baixo risco:** melhorar nomes de exibição, mantendo chaves/FKs. “Foundations” é tag/agrupamento de trilha; não retira StereoPosition/GuessFrequency de suas áreas.
“Continuous matching” é dinâmica de resposta, hoje de EQ Matching, não sétima categoria. “Mixing” é a experiência futura em `Mix`; MixProblem fica em `Audio/CriticalListening`.

### 5.3 Plano incremental de modelo e UX

1. **PR de descoberta:** adicionar aba/agrupamento por categoria, badges de categoria nos cards e ocultar seções de tipo sem exercícios.
2. Reusar `Exercise.ExerciseCategory` e a ordem de `TrainingTracks.Audio`; carregar categorias explicitamente para a view.
3. **Sem migration:** os lookup rows e a FK já existem; seed já resolve IDs por nome em bases não vazias. [SEED:14–43; UTSEED:27–57]
4. Não renomear/remover/reordenar o array de categorias por ordinal; novos lookups, se necessários no futuro, são append-only.
5. **PR seguinte, combinável:** filtros GET `track`, `category`, `type`, `level`, aplicados ao mesmo conjunto de cards.
6. Exemplo de navegação: `/Exercise?track=audio&category=FrequencyEq&level=intermediate`.
7. Validar chaves contra a track selecionada; valor desconhecido não deve voltar silenciosamente para “todos”.
8. `level` filtra perfis declarados; o Start injeta o filtro real, como `gqLevel=intermediate`, não `DifficultyLevelId=2`.
9. Preservar `/Exercise`, `/Exercise?track=audio`, `#trackTabs`, `#byDifficulty`, URLs de exercícios e o padrão Beginner sem query.
10. `type` permanece modelo existente; `AudioComparison` é o único tipo disponível em Áudio inicialmente.
11. Para distinguir seleção/código/contínuo, adicionar descritor **code-first** opcional `AnswerMode`, sem trocar os type IDs atuais.
12. Tags `Foundations` e `ContinuousMatching` também podem ser code-first; não entram em DailyChallenge nem definem track.
13. Combinação vazia recebe mensagem localizada, botão limpar filtros e contagem de exercícios únicos, não de cards repetidos por nível.
14. A forma de apresentação pode permanecer por nível/tipo/categoria; o filtro de categoria deve funcionar nas três.
15. Em 360 px, selects empilhados/colapsáveis com resumo visível; não uma barra horizontal de sete pills sem wrap.
16. Navegação e formulário GET funcionam sem JavaScript; melhoria progressiva atualiza contagem/status sem perder foco.

### 5.4 Localização: reutilização e novas chaves

Chaves de categoria já existem nas três culturas. Traduzir **DisplayName visual**, sem mudar `Name` persistido.
As traduções abaixo são propostas editoriais, não uma transcrição dos recursos atuais. [LOC:659–674]

| Chave | en-US proposto | pt-BR proposto | fr-CA proposto |
|---|---|---|---|
| `ExerciseCategory.Level` | Level and gain | Nível e ganho | Niveau et gain |
| `ExerciseCategory.FrequencyEq` | Frequency and EQ | Frequência e EQ | Fréquence et égalisation |
| `ExerciseCategory.Dynamics` | Dynamics | Dinâmica | Dynamique |
| `ExerciseCategory.StereoPhase` | Stereo and polarity | Estéreo e polaridade | Stéréo et polarité |
| `ExerciseCategory.SpaceTime` | Space and time | Espaço e tempo | Espace et temps |
| `ExerciseCategory.CriticalListening` | Critical listening and diagnosis | Escuta crítica e diagnóstico | Écoute critique et diagnostic |
| `Exercise.Filter.Category` | Category | Categoria | Catégorie |
| `Exercise.Filter.Type` | Response type | Tipo de resposta | Type de réponse |
| `Exercise.Filter.Level` | Level | Nível | Niveau |
| `Exercise.Filter.AllCategories` | All categories | Todas as categorias | Toutes les catégories |
| `Exercise.Filter.AllTypes` | All types | Todos os tipos | Tous les types |
| `Exercise.Filter.AllLevels` | All levels | Todos os níveis | Tous les niveaux |
| `Exercise.Filter.ResultsCount` | {0} exercises | {0} exercícios | {0} exercices |
| `Exercise.Filter.Empty` | No exercises match these filters. | Nenhum exercício corresponde a esses filtros. | Aucun exercice ne correspond à ces filtres. |
| `Exercise.Filter.Clear` | Clear filters | Limpar filtros | Effacer les filtres |
| `Exercise.Tag.Foundations` | Foundations | Fundamentos | Fondements |
| `Exercise.Tag.ContinuousMatching` | Continuous matching | Correspondência contínua | Appariement continu |

Se o filtro continuar representando `ExerciseType`, preferir label **Type/Tipo/Type**.
Usar “Response type” somente quando `AnswerMode` existir; não prometer uma distinção que o modelo atual não faz.
Tratar contagem singular/plural e tipografia fr-CA; nomes de keys/IDs não são traduzidos.

### 5.5 Atribuição final dos 27 exercícios

Nenhuma mudança de FK é necessária para a atribuição abaixo: coincide com o seed/catálogo atuais. [SEED:1605–1730; CAT]

| Exercício | Categoria canônica | Tag/dinâmica auxiliar proposta |
|---|---|---|
| Level Match / `LevelMatch` | `Level` | Foundations; identificação/estimação discreta |
| Stereo Position / `StereoPosition` | `StereoPhase` | Foundations; posição discreta |
| Guess Frequency / `GuessFrequency` | `FrequencyEq` | Foundations; frequência discreta |
| Boost or Cut / `BoostOrCut` | `FrequencyEq` | Direção + região |
| Guess the Q / `GuessQ` | `FrequencyEq` | Largura; Q numérico no A |
| Compression / `Compression` | `Dynamics` | Detecção A/B |
| Compressor Attack / `CompressorAttack` | `Dynamics` | Timing comparativo |
| Compressor Release / `CompressorRelease` | `Dynamics` | Timing comparativo |
| Compression Ratio / `CompressionRatio` | `Dynamics` | Classificação |
| Stereo Width / `StereoWidth` | `StereoPhase` | Classificação |
| Channel Polarity / `ChannelPolarity` | `StereoPhase` | Diagnóstico binário |
| Hear the Reverb / `ReverbPresence` | `SpaceTime` | Detecção A/B |
| Reverb Decay / `ReverbDecay` | `SpaceTime` | Timing comparativo |
| Reverb Pre-delay / `ReverbPreDelay` | `SpaceTime` | Timing comparativo |
| Echo Timing / `EchoTime` | `SpaceTime` | Tempo discreto |
| Find the Distortion / `Distortion` | `CriticalListening` | Detecção A/B |
| High-pass cutoff / `HighPassCutoff` | `FrequencyEq` | Cutoff discreto |
| Low-pass cutoff / `LowPassCutoff` | `FrequencyEq` | Cutoff discreto |
| Low shelf / `LowShelf` | `FrequencyEq` | Direção de shelf |
| High shelf / `HighShelf` | `FrequencyEq` | Direção de shelf |
| EQ gain / `EqGain` | `FrequencyEq` | Magnitude discreta |
| Compressor threshold / `CompressorThreshold` | `Dynamics` | Comparação |
| Echo feedback / `EchoFeedback` | `SpaceTime` | Persistência comparativa |
| Reverb mix / `ReverbMix` | `SpaceTime` | Quantidade discreta |
| Find the noise / `NoiseDetection` | `CriticalListening` | Detecção A/B |
| Find the mix change / `MixProblem` | `CriticalListening` | Diagnóstico + ação candidata |
| EQ Matching / `EqMatching` | `FrequencyEq` | ContinuousMatching |

### 5.6 Impactos fora do catálogo

- **Dashboard:** reutilizar `ByCategory`; gráfico por categoria é mais informativo que a única dimensão de tipo útil de Áudio, `AudioComparison`. [REPORT:42–47; DASH:284–291; ITP:77–89]
- **DailyChallenge:** somente expor categorias não altera sorteio; mudar categorias/nomes/pool altera resultados inclusive ao recomputar dias passados. [DRULE:75–94,143–185]
- **Trilha:** categorias não são pré-requisitos; mostrar percurso recomendado por domínio sem mudar ainda a avaliação cronológica.
- **Rotinas:** presets de exercício continuam autoritativos; filtro de catálogo não pode sobrescrever o nível travado pelo professor. [CTRL:225–229; FVIEW:67–91]
- **Badges/games:** manter pools legados Music-only; os sete badges Audio agora têm regras próprias. Games/placement continuam separados. [TRACK:8–12; BRULE:83–91,345–378; GAMES:38–43]
- **Futuro Mix:** categorias precisam de chaves próprias, como `MixVocals`/`MixDrums`, porque uma categoria pertence a uma só track. Não reutilizar `Dynamics` em duas tracks. [TRACK:29–32]
- **Migration só quando houver schema novo:** múltiplas categorias relacionais, `TrackId` persistido, métricas tipadas ou versão de trilha por usuário. A UX proposta não demanda nenhum deles.
- **Testes:** INDEXTEST, UTSEED, UTCATALOG, TRACKTEST, ITP e E2E precisam manter isolamento Music/Audio, URLs e culturas.

## 6. Revisão de níveis

### 6.1 Três coisas diferentes chamadas “nível”

1. `DifficultyLevelId` seed: classificação/default legado de entrada, **1 em todos os Áudio**. [SEED:1590–1732]
2. `*Level` no filtro: perfil B/I/A efetivamente escolhido e aplicado pelo gerador. [GEN:179–195; CAT]
3. Nível/XP do jogador: progressão global de gamificação, não dificuldade da rodada. [XP:5–38]

O catálogo já usa perfis suportados, e o dashboard de Áudio lê o nível salvo em `FilterJson`.
Portanto, não há evidência de “só beginner” nem de ausência de níveis intermediário/avançado nos 27 exercícios. [INDEX:9–17; REPORT:108–143]

### 6.2 Parâmetros compartilhados e notação

- B/I/A = beginner/intermediate/advanced; `P` = pico dBFS da fonte registrado na metadata, **antes** do processamento.
- “a/b/c” dentro de um nível é o conjunto de alternativas; “x vs y” é um par comparado, com atribuição de lados aleatória quando a pergunta é A/B.
- Peaking EQ: frequência em Hz, ganho em dB, Q adimensional; os ganhos são antes de loudness matching.
- EQ de corte: Butterworth de segunda ordem, 12 dB/oct; shelf com slope unitário. [DSP:380–405; DSPPLAN:33–42]
- Compressor: detector peak ligado entre canais, hard-knee, sem makeup implícito/lookahead/limiter. [DSP:439–458]
- Width: multiplicador de Side; 100% = 1, não uma medida geométrica da posição do som. [DSP:461–472]
- Reverb: decay **nominal** de uma rede com damping, não RT60 broadband medido. Echo/reverb não acrescentam frames de cauda. [TIME; EXP:207–216]
- Canonical matching: tolerância de 0.1 LU por clip, até 0.2 LU entre dois clips; isso é contrato, não resultado de medição realizado nesta revisão. [DSP:35–37; DOC:390–398]

### 6.3 Tabela completa de níveis atuais — fatos

| Exercício / filtro | Beginner atual | Intermediate atual | Advanced atual | Constantes, resposta e referência |
|---|---|---|---|---|
| LevelMatch / `lmLevel` | Δ6/9/12 dB; resposta A/B | Δ2/3/4/6 dB; A/B + Δ | Δ0.5/1/1.5/2 dB; A/B + Δ | Fonte de nível com dificuldade 1/2/3; offset comum aleatório em passos de 0.5 dB sobre janela de 6 dB, limitado por headroom; sem LUFS matching. [GEN:27–33,383–446] |
| StereoPosition / `spLevel` | L −1, C 0, R +1 | L75 −.75, L25 −.25, C, R25 +.25, R75 +.75 | L75/L50/L25/C/R25/R50/R75, pan −.75/−.5/−.25/0/.25/.5/.75 | Fonte mono; pan equal-power; apenas clip A; seleção exata de código. [GEN:35–47,343–380] |
| GuessFrequency / `gfLevel` | 100/500/1000/5000/10000; +9 dB; Q1 | 125/250/500/1000/2000/4000/8000; +6 dB; Q1 | 500/630/800/1000/1250/1600/2000; +6 dB; Q2 | Quatro opções; boost em A ou B; alvo comum −26…−32 LUFS em passos de .5; fontes qualificadas para o perfil inteiro. [GEN:49–69,104–169,332–340] |
| BoostOrCut / `bcLevel` | ±9 dB | ±6 dB | ±3 dB | **Todos os níveis:** sub100/bass125/lowMids250/mids1000/upperMids2000/presence4000/air8000 Hz; Q1; A ref/B EQ. [GEN:197–228] |
| GuessQ / `gqLevel` | Q .35/1/4, broad/medium/narrow | Q .5/1.5/4, mesmas categorias | Q .5/1/2/4/8, resposta numérica discreta | Centro 500/1000/2000 aleatório; +9 dB; A ref/B boost; fonte do perfil EQ intermediate. [GEN:230–248] |
| Compression / `cpLevel` | threshold −28 dBFS; 6:1 | threshold −24; 3:1 | threshold −22; 2:1 | Attack 0 ms, release20 ms; original vs comprimido com lado aleatório; subset de compressão, não selecionado por level. [GEN:251–267,305–311] |
| CompressorAttack / `caLevel` | 0 vs60 ms | 1 vs30 ms | 3 vs15 ms | Threshold −28 dBFS; 6:1; release80 ms; ambos comprimidos; lado rápido aleatório. [EXP:24–31,200–205] |
| CompressorRelease / `crLevel` | 20 vs350 ms | 40 vs200 ms | 70 vs130 ms | Threshold −28 dBFS; 6:1; attack0 ms; ambos comprimidos; lado rápido aleatório. [EXP:32–39] |
| CompressionRatio / `raLevel` | 2/4/8:1 | 2/3/6:1 | 2/3/4:1 | Threshold −28 dBFS; attack0/release20 ms; B tem ratio sorteado; light/medium/strong relativos ao perfil. [EXP:40–48] |
| StereoWidth / `swLevel` | .45/1/1.55, ou45/100/155% Side | .65/1/1.35 | .8/1/1.2 | A ref; B narrow/same/wide; pools de width próprios no storage, full-mix no bundled. [EXP:49–56,242–250; LIB:250–253] |
| ChannelPolarity / `poLevel` | Normal ou canal R invertido; pool B privado | Mesmo processamento; pool I privado | Mesmo processamento; pool A privado | Inversão exata; no bundled, **full-mix em todos os níveis**, sem mudança de complexidade. [EXP:57–62,242–250; LIB:255–268] |
| ReverbPresence / `rvLevel` | Wet .55 | Wet .40 | Wet .25 | Decay1.1 s; pre-delay15 ms; lado com reverb adicionado aleatório. [EXP:63–68] |
| ReverbDecay / `dcLevel` | .4 vs3 s | .7 vs2 s | 1 vs1.5 s | Wet .60; pre-delay15 ms; lado com decay maior aleatório. [EXP:69–76] |
| ReverbPreDelay / `rpLevel` | 0 vs100 ms | 10 vs70 ms | 25 vs50 ms | Decay .7 s; wet .60; lado com pre-delay maior aleatório. [EXP:77–84] |
| EchoTime / `etLevel` | 75/225/675 ms | 100/200/400 ms | 150/225/300 ms | Feedback .30; wet .35; B processado; short/medium/long. [EXP:85–93] |
| Distortion / `dsLevel` | Knee `P−12` dBFS | Knee `P−9` | Knee `P−6` | Transferência `threshold*tanh(input/threshold)`; original/processado com lado aleatório. [EXP:94–99; DSP:482–487] |
| HighPassCutoff / `hpfLevel` | 80/250/800 Hz | 120/250/500 Hz | 180/250/350 Hz | HPF Butterworth 12 dB/oct; B processado; low/medium/high. [EXP:100–121; DSP:380–387] |
| LowPassCutoff / `lpfLevel` | 1500/4500/12000 Hz | 2500/4500/8000 Hz | 3500/4500/6000 Hz | LPF Butterworth 12 dB/oct; B processado; low/medium/high. [EXP:100–121] |
| LowShelf / `lsLevel` | ±9 dB | ±6 dB | ±3 dB | Shelf150 Hz; slope1; resposta boost/cut. [EXP:122–130; DSP:390–405] |
| HighShelf / `hsLevel` | ±9 dB | ±6 dB | ±3 dB | Shelf4000 Hz; slope1; resposta boost/cut. [EXP:122–130] |
| EqGain / `egLevel` | 3/7.5/12 dB | 3/6/9 dB | 3/4.5/6 dB | Bell1000 Hz/Q1; só boosts; light/medium/strong. [EXP:131–143] |
| CompressorThreshold / `ctLevel` | `P−15` vs `P−3` dBFS | `P−12` vs `P−4` | `P−10` vs `P−5` | Ratio4:1; attack3/release50 ms; ambos comprimidos; escolher menor threshold. [EXP:144–151] |
| EchoFeedback / `efLevel` | .10 vs .65 | .20 vs .50 | .30 vs .45 | Delay250 ms; wet .50; ambos processados; maior feedback aleatório. [EXP:152–159] |
| ReverbMix / `rmLevel` | 0/.15/.40/.70 | 0/.20/.40/.60 | 0/.30/.40/.50 | Decay1.2 s; pre-delay20 ms; B dry/light/medium/strong; dry significa não acrescentar reverb. [EXP:160–172] |
| NoiseDetection / `ndLevel` | SNR12 dB | SNR21 dB | SNR30 dB | White noise relativo ao RMS da fonte, seed aleatório positivo; lado ruidoso aleatório. [EXP:173–178,240; DSP:421–437] |
| MixProblem / `mpLevel` | EQ ±6 dB; width .50 | EQ ±4 dB; width .65 | EQ ±2 dB; width .80 | Muddy +250/Q1, harsh +4000/Q1, boomy +125/Q1, thin −150/Q1, narrow Side; correções inversas e mesmo alvo LUFS. [EXP:179–199,208–238] |
| EqMatching / `eqmLevel` | F250–4000 Hz; \|G\|3–6 dB; Q .7–3 | F120–8000; \|G\|3–9; Q .5–6 | F60–12000; \|G\|3–12; Q .5–8 | F/Q uniformes em log; magnitude G uniforme e sinal aleatório; fonte EQ intermediate; A ref/B alvo; −30…−34 LUFS; três parâmetros contínuos. [GEN:270–302] |

**Matching comum:** BoostOrCut/GuessQ/Compression usam alvo aleatório −28…−32 LUFS; expansão −30…−34 LUFS; steps de 0.5.
Expansão adiciona fade-out terminal de 15 ms aos clips, inclusive reparos. [GEN:317–326; EXP:14–15,207–216]
Pools da expansão: estéreo; no bundled full-mix; no storage width/polarity têm regras próprias e os demais usam subset de compressão, independentemente do nível. [EXP:242–250]
**Pools de polarity:** prefixes `pixabay-{id}-`, sempre estéreo; B412230/445661; I484622/513718/513744/537751; A323181/513717/541097/550792/597971. O codebook seleciona gravações, não graus de inversão. [LIB:255–268]
Intervalos contínuos têm extremidade superior praticamente exclusiva por `UnitRandom`; as faixas acima representam o perfil, não garantia de sorteio de cada endpoint. [GEN:280–286,303]

### 6.4 Regras de validação atuais

| Exercício/dinâmica | Regra real | Consequência pedagógica |
|---|---|---|
| LevelMatch | Lado exato; diferença numérica com erro <.001 dB, quando exigida | “Estimativa” é escolha exata de alternativa, não tolerância auditiva livre. [VAL:176–193] |
| StereoPosition | Código exato, case-insensitive/trim | Não há crédito parcial por posição vizinha. [VAL:206–215] |
| GuessFrequency | String da frequência exata | Bandas próximas contam como erro completo. [VAL:225–238] |
| BoostOrCut / GuessQ / Compression | Campo `answer` exato | Direção/região ou categoria errada não recebe crédito parcial. [VAL:247–297] |
| Expansão | Campo `answer` exato; detalhes com parâmetros | Não valida uma estimativa contínua ou ação alternativa plausível. [EV:17–27] |
| EqMatching | Score ponderado, correto quando **>0.8**, não ≥0.8 | Precisão parcial aparece no feedback, mas history/XP convertem para correto/erro. [EQV:8–36; CTRL:616–640] |

Para EQ Matching:

- Pesos: frequência50%, ganho30%, Q20%. [EQV:8–10]
- Erro F = `abs(log2(guess/target))`; crédito total até **1/6 de oitava = 200 cents**; zero em uma oitava.
- Erro G = diferença absoluta; crédito total até **1.5 dB**; zero em6 dB.
- Erro Q = distância logarítmica; crédito total até razão1.2; zero em razão2.
- A tolerância de Q significa `target/1.2 ≤ guess ≤ target*1.2`, não intervalo simétrico de ±20% linear.
- Fora da zona de crédito total, cada erro cresce linearmente até1; a média ponderada define o score.
- Validator aceita F20–20000, G−12…12, Q.25…8; UI limita F60–12000. Não é vazamento, mas convém documentar diferença de bounds. [EQV:22–25; EQJS:17–18]

**Derivação verificada da fórmula, não execução da aplicação:** alvo `1000|3|1`, palpite `1000|0|1` → **90%/correto**.
Com o mesmo alvo, palpite `1000|-1|1` → **83.33%/correto**, embora o alvo seja boost e o palpite cut.
O exemplo2500/+5/Q1.4 →2700/+4.6/Q1.2 do README recebe100%, não91%: os três erros estão na região de crédito total. Isso confirma que a tolerância atual é bastante generosa.
Isso não é erro aritmético: é o efeito de permitir compensação entre dimensões com o corte global >80%.
**Proposta:** conservar o percentual como feedback, mas decidir requisito de direção correta e piso de acurácia por parâmetro para `IsCorrect`.

### 6.5 Inconsistências e pontos de atenção encontrados

| Achado | Fato / avaliação | Prioridade / risco |
|---|---|---|
| Todos seed1 versus trilha longa | Fato: defaults B; não é bug de níveis suportados. Avaliação: ranking por seed não informa complexidade entre exercícios. [SEED; CATALOG:23–25] | P1; baixo se somente explicar |
| Catálogo já mostra B/I/A | Fato: cada Audio aparece em todos os níveis declarados; Start preseleciona filtro. Não repropor essa correção como trabalho pendente. [INDEXTEST:60–152] | Preservar; risco de regressão |
| BoostOrCut 100 vs125 Hz | Fato: diferença de386.31 cents com Q1; mesmas sete regiões no B. Avaliação: candidatos graves se sobrepõem fortemente e “sub100” é uma nomenclatura pouco didática. [GEN:201–205] | P0; M, nova qualificação |
| Pool bundled de BoostOrCut depende da resposta | Fato: fontes são escolhidas por banda; synth-chords pode servir 1k/2k, mas não100/4k/8k. Storage usa pool EQ comum. Avaliação: timbre pode eliminar alternativas no local/CI. [GEN:64–69,206,221–228; SOURCES] | P0; M |
| GuessQ varia centro desconhecido já no B | Fato: +9 fixo, centro sorteado; README previa centro conhecido. Avaliação: inicia com generalização antes de isolar Q. [GEN:232–248; DESIGN:152–158] | P1; S–M |
| ChannelPolarity B/I/A idênticos no bundled | Fato: só full-mix; labels prometem materiais de complexidade crescente. Storage troca pools, com testes de energia M/S opt-in. [EXP:242–250; LIB:255–268; PRIVATE:39–68; LOC:5294–5296] | P1; M |
| ReverbMix dry fica mais distante no A | Fato: gap dry→light .15/.20/.30; gaps wet reduzem. Avaliação: o nível não fica mais difícil em todas as alternativas. [EXP:160–170] | P1; M |
| EqMatching aumenta ganho máximo no A | Fato: máximos6/9/12, mínimo3; faixa F/Q amplia, ganho pode ficar mais evidente. Avaliação: mais variáveis não garante sutileza monotônica. [GEN:275–286] | P1; M |
| EqMatching qualifica fonte para outro intervalo | Fato: fonte é `FrequencySourcesFor("intermediate",1000)` em todos os níveis; não uma regra própria para60–12000/Q8. [GEN:287; GEN:157–169] | P0; M–L, avaliar extremos |
| Preview RMS versus canonical LUFS | Fato: normalizações diferentes. Avaliação: o aluno pode perseguir diferença de balanço/loudness não causada só por erro de parâmetro. [ENGINE:273–305; DSP:500–534] | P1; M |
| Rótulos de nível versus números no código | Os conjuntos numéricos dos três resx conferem com os geradores; não encontrei divergência atual como antigo Q avançado. A divergência está no DOC e no material bundled de polarity. [LOC; GEN; EXP] | P1; S |
| Decimais na UI | LM usa “0.5/1.5” literais e feedback com `String`; Q avançado usa “Q0.5” literal. Recursos pt-BR/fr-CA usam vírgula. [LM:31–37; LMJS:120–129; VIEW:103–107] | P1; S |
| Decay nominal versus RT60 | Fato: rede com damping e sem cauda anexada; não um erro nos resx, que já avisam nominal. Não prometer medição física do recinto. [TIME; LOC:5320] | P1; risco editorial |
| Trilha ensina só B | Fato: todos os presets Audio são beginner; conclusão não prova domínio I/A. [PATH:68–94] | P1; mudar critérios exige decisão |
| Ordem reflete batches | Fato: HPF/LPF/shelves/EQ gain vêm depois de espaço/distorção; threshold e ReverbMix chegam tarde. Avaliação: há oportunidade de pré-requisitos mais coerentes. [PATH; CAT] | P2; alto para históricos |

### 6.6 Justificativa psicoacústica e calibração

As recomendações abaixo são **hipóteses de desenho**, não afirmações de que um único JND serve para todos.

- **Nível:** ~1 dB é uma ordem de grandeza útil de discriminação em condições controladas; a referência pública OpenStax descreve isso em frequência fixa. Não transportar automaticamente esse número para música/ambiente/dispositivo. [PSY1]
- **Level Match A:** diferenças0.5–2 dB tornam o objetivo fino; identificar o lado e nomear um dos quatro deltas são duas habilidades diferentes. Replay e comparação sincronizada ajudam a memória auditiva.
- **EQ:** ±3 dB não é um limiar universal. Q, energia na banda, frequência, loudness matching, mascaramento e instrumento determinam a pista.
- **Frequência:** o JND de pitch de tom puro não mede identificação do centro de um bell em música. Bandas de1/3 oitava são uma resolução curricular, não um limite fisiológico.
- **Pan:** coordenada−1…1 expressa a lei do pan do código, não ângulo espacial; headphones, caixas, assimetria de audição e saída mono alteram o resultado.
- **Width:** perceptibilidade depende de haver Side; por isso uma mudança20% não tem dificuldade equivalente em qualquer gravação.
- **Compressão:** ratio sozinho não determina audibilidade; quantidade acima do threshold, crest factor, duração dos picos e detector temporal importam.
- **Attack/release:** separações em ms precisam ser avaliadas sobre eventos adequados; 70 vs130 ms pode ser audível em um groove e pouco informativo em sustain.
- **Reverb/echo:** diferenças entre coeficientes wet/feedback não são JNDs perceptuais; pausas, densidade e masking das reflexões pesam muito.
- **Ruído:** SNR RMS é medida objetiva; audibilidade depende do espectro do ruído e do material que o mascara.
- **Saturação:** knee relativa ao pico evita sinal quase inalterado por ser silencioso, mas não garante quantidade igual de distorção em todos os envelopes.
- **Escuta segura:** nunca pedir que o aluno aumente muito o volume para “passar no avançado”; recomendar volume confortável, pausas e equipamento estéreo quando necessário.
- **Calibração proposta:** comparar B/I/A com pessoas iniciantes e experientes, pelo menos dois tipos de fonte e fones/caixas; revisar taxa de acerto, confusão entre vizinhos e comentário de escuta.
- **Critério de aceite:** B deve ensinar fenômeno; I deve exigir refinamento; A deve exigir precisão/generalização. Diferença numérica positiva não basta.
- **Referência metodológica:** usar ITU-R BS.1116 como ponto de partida para desenho de avaliações auditivas; esta revisão confirmou o registro da recomendação, não auditou aderência à norma. [PSY2]

### 6.7 Tabela revisada proposta — antes → depois

**Proposta, não implementação:** “manter” é evitar mudanças sem evidência de escuta; valores atuais na tabela6.3. Versionar mudanças, sem reclassificar tentativas antigas silenciosamente.

| Exercício | Antes | Depois proposto B → I → A | Justificativa | Risco / esforço |
|---|---|---|---|---|
| LevelMatch | 6/9/12; 2/3/4/6; .5/1/1.5/2 | Manter os conjuntos; separar “lado” de “Δ” no feedback; modo trim futuro com tolerâncias2/1/.5 dB, claramente distinto | Preserva design original e separa detecção de estimação; .5 dB não é meta universal | Baixo na UI; M no contínuo |
| StereoPosition | 3→5→7 posições | Manter; usar rail/pan e feedback de distância; posições mais finas só depois de qualificação estéreo | Progressão atual é lógica; não exige grid1% prematuramente | Baixo / S–M |
| GuessFrequency | +9/Q1; +6/Q1; +6/Q2 e sete bandas500–2k | Manter inicialmente; ampliar A por janelas ISO qualificadas de graves/médios/agudos, mantendo quatro opções próximas | Preserva frequência como variável única e melhora generalização | Médio / M–L |
| BoostOrCut | Mesmas sete regiões,100/125 próximos; ±9/6/3 | B: três centros250/1000/4000, ±9; I: cinco125/250/1000/4000/8000, ±6; A: sete63/125/250/1000/2000/4000/8000, ±3, Q1 | Poucas escolhas no início; sub mais distinto;63 Hz somente com fontes qualificadas | Alto / M, retestar fontes |
| GuessQ | Centro aleatório em B; três→cinco Q | B: centro público fixo1k,+9,Q.35/1/4; I: centros500/1k/2k,Q.5/1.5/4; A: manter cinco Q e fonte variada | Isola largura antes de generalização; constante pública não revela alvo sorteado | Médio / S–M |
| Compression | −28/6:1 →−24/3:1 →−22/2:1;0/20 ms | Manter; estender a qualificação crest3/2/1 dB já testada nos privados e diversificar fontes transientes, sem impor “ratio=audibilidade” | Critério de saída é mais relevante que só configuração | Médio / M |
| CompressorAttack | 0/60 →1/30 →3/15 ms | Manter números; B usa fonte transiente/clara, I groove, A material variado **só quando aprovado** | Os gaps são coerentes; aumentar realismo pelo contexto, não por meter | Médio / M |
| CompressorRelease | 20/350 →40/200 →70/130 ms | Manter; selecionar eventos com recuperação audível, conferir relação com ritmo | Release precisa de espaço entre eventos para ensinar | Médio / M |
| CompressionRatio | 2/4/8 →2/3/6 →2/3/4:1 | Manter; mostrar valores de todas as alternativas e associar cada label ao perfil; matching contínuo posterior | Contraste menor no A é plausível, mas não é GR absoluta | Baixo/Médio / S–M |
| StereoWidth | Side45/100/155 →65/100/135 →80/100/120% | Manter; aplicar qualificação de energia Side também ao catálogo local | Diferenças reduzem corretamente; materiais precisam sustentar a pista | Médio / M |
| ChannelPolarity | Mesmo toggle; pools só no storage | Manter processamento; qualificar materiais B/I/A em ambos os modos; até existir isso, rotular bundled como dificuldade única de material | Inversão é binária; dificuldade deve vir da fonte, não de graus de inversão | Médio / M–L |
| ReverbPresence | Wet55→40→25% | Manter; tornar fonte/pause suitability explícita e comparar sempre “reverb adicionado” | Progressão de dose é plausível; original pode conter ambience | Baixo/Médio / S–M |
| ReverbDecay | .4/3 →.7/2 →1/1.5 s | Manter; melhorar recortes/lacunas de escuta e feedback nominal; cauda anexada só em PR de DSP próprio | Gap temporal diminui; material contínuo pode mascarar a cauda | Médio / M |
| ReverbPreDelay | 0/100 →10/70 →25/50 ms | Manter; usar ataques isoláveis e não prometer início de reverberação exatamente igual ao pre-delay nominal | Rede também contém atrasos internos; alvo é mudança controlada | Médio / M |
| EchoTime | 75/225/675 →100/200/400 →150/225/300 ms | Manter; mostrar valores de candidatos; exercício BPM/sync separado, se aprovado | Separa delay discreto de subdivisão musical | Baixo / S |
| Distortion | Knee P−12/−9/−6 | Manter; usar source qualification de grão/harmônicos e corrigir “curva”→knee na terminologia pt-BR | Dose reduz; não equivale a modelos analógicos/clipping | Médio / S–M |
| HighPassCutoff | 80/250/800 →120/250/500 →180/250/350 | Manter; curvas-candidatas/Hz e garantia de energia baixa por fonte | Aproximação em frequência reduz contraste de modo coerente | Baixo/Médio / S–M |
| LowPassCutoff | 1.5/4.5/12k →2.5/4.5/8k →3.5/4.5/6k | Manter; confirmar detalhe de agudos e oferecer alternativa de janela a quem não percebe12k, sem diagnóstico clínico | Diferentes equipamentos/audição afetam extremos | Médio / M |
| LowShelf | ±9→±6→±3 @150 | Manter; separar feedback de ganho global versus platô espectral | Bom eixo de dificuldade, dependente de conteúdo grave | Baixo / S |
| HighShelf | ±9→±6→±3 @4k | Manter; validar brilho em fontes não excessivamente escuras | Redução de contraste é coerente | Baixo/Médio / S–M |
| EqGain | 3/7.5/12 →3/6/9 →3/4.5/6 | Manter; botões incluem dB/label; acrescentar cuts como modo posterior, não mudar tudo junto | Gap4.5→3→1.5 dB aumenta precisão espectral | Baixo / S |
| CompressorThreshold | P−15/−3 →P−12/−4 →P−10/−5 | Manter; feedback mostra referencialP e efeito medido; não chamar threshold de input gain | Distância12→8→5 dB reduz contraste; fonte ainda é determinante | Médio / M |
| EchoFeedback | 10/65→20/50→30/45% | Manter; qualificar repetições posteriores e fonte com lacunas | Gap55→30→15 pontos diminui; primeiro repeat não diferencia feedback | Médio / M |
| ReverbMix | 0/15/40/70 →0/20/40/60 →0/30/40/50% | B0/25/50/75%; I0/20/35/50%; A0/15/25/35%; decay1.2/pre20 | Proposta torna menores **todos** os gaps, incluindo dry→light; depende de escuta para calibrar dose | Alto / M |
| NoiseDetection | SNR12→21→30 | Manter; só ampliar para36–40 depois de validar ruído ambiente/mascaramento | Progressão forte e clara, sem usar silêncio como pista artificial | Médio / M |
| MixProblem | EQ6/4/2; width.50/.65/.80 | Manter magnitudes; B oferece três hipóteses bem separadas, I/A cinco; A contextualiza instrumento antes de multiplicar problemas | Cinco defeitos já no B sobrecarregam; um defeito por rodada continua válido | Médio / M |
| EqMatching | F/Q maiores e \|G\| máximo6/9/12; tolerância única | B:F250–4k,\|G\|6–9,Q.7–1.4; I:F120–8k,\|G\|4–8,Q.5–4; A:F60–12k,\|G\|3–6,Q.5–8; fonte própria e regra de qualidade por componente | Aumenta precisão e amplitude curricular sem tornar o A mais evidente por ganho maior | Alto / M–L |

**EQ Matching, proposta de tolerâncias por perfil:** crédito total F300/200/100 cents, G2/1.5/.75 dB, razão Q1.35/1.2/1.15.
Conservar pesos50/30/20 como referência do documento; avaliar piso de50% por componente e direção de ganho correta.
Os limites de zero-crédito e a definição de `IsCorrect` precisam ser aprovados e testados juntos; esses números são proposta experimental, não JNDs publicados.
Antes de mudar números, acrescentar casos de teste de alvo±3 dB, ganho zero/sinal oposto, score exatamente80% e vizinhança das tolerâncias.

### 6.8 A trilha atual é uma progressão sensata?

**Parcialmente.** O trio inicial é coerente; direção/Q antes de matching também.
A sequência seguinte acompanha a ordem dos batches do catálogo, não apenas pré-requisitos pedagógicos. [PATH:68–94; CAT:8–36]

| Trecho atual | Avaliação | Ordem sugerida para guia futuro |
|---|---|---|
| 1–3 LevelMatch/StereoPosition/GuessFrequency | Bom começo e pouca dependência teórica | Conservar |
| 4–5 BoostOrCut/GuessQ | Faz sentido; Q pode precisar de centro fixo introdutório | Direção, shelves/cutoffs, Q, ganho |
| 6–9 Compression/Attack/Release/Ratio | Funciona, mas ratio/threshold fundamentam o funcionamento temporal | Compression→Ratio→Threshold→Attack→Release |
| 10–11 Width/Polarity | Coerente após pan | Conservar bloco |
| 12–15 Presence/Decay/PreDelay/EchoTime | ReverbMix tarde separa quantidade de seus pré-requisitos | Presence→Mix→Decay→PreDelay→EchoTime→Feedback |
| 16 Distortion;17–21 filtros/shelves/EQGain | EQ básico chega depois de vários efeitos e antes de reabrir dynamics | Mover conceitualmente para o bloco FrequencyEq |
| 22–25 Threshold/Feedback/ReverbMix/Noise | São retornos a domínios já apresentados | Agrupar por domínio, não batch |
| 26 MixProblem;27 EqMatching | Capstones úteis, mas matching de EQ pode preparar diagnóstico tonal | EQMatching fecha EQ; MixProblem fecha percurso |

**Sequência candidata completa, sem eliminar exercícios:**
1 LevelMatch →2 StereoPosition →3 GuessFrequency →4 BoostOrCut →5 LowShelf →6 HighShelf →7 HighPassCutoff →8 LowPassCutoff →9 GuessQ →10 EqGain →11 EqMatching →12 Compression →13 CompressionRatio →14 CompressorThreshold →15 CompressorAttack →16 CompressorRelease →17 StereoWidth →18 ChannelPolarity →19 ReverbPresence →20 ReverbMix →21 ReverbDecay →22 ReverbPreDelay →23 EchoTime →24 EchoFeedback →25 Distortion →26 NoiseDetection →27 MixProblem.

**Não implementar essa reordenação como alteração cosmética:**
- O progresso é recalculado dos answers; cada passo só conta respostas após a conclusão do anterior. [PEVAL:30–93; PSVC:75–144]
- Reordenar pode fazer passos já concluídos deixarem de concluir com o mesmo histórico.
- Os presets são orientação; o evaluator não filtra por `lmLevel`/etc. Sete acertos já podem concluir antes de dez tentativas. [PATH:15–19; PEVAL:65–71]
- Primeiro publicar um **guia por categoria** sem alterar o evaluator; uma Audio path v2 exige opt-in/política de preservação e versão persistente ou mecanismo equivalente.
- Não duplicar todo o caminho três vezes B/I/A sem decidir se a aprovação mede reconhecimento inicial ou domínio avançado.

### 6.9 Sobreposição de propósito: o que manter e o que não multiplicar

| Pares/famílias | Distinção atual | Decisão sugerida |
|---|---|---|
| GuessFrequency / BoostOrCut / EqGain / GuessQ / EqMatching | Centro / direção-região / magnitude / largura / combinação | Manter; descrever pré-requisitos e reduzir confusão de nomes. [GEN; EXP; EQV] |
| LowShelf / HighShelf / BoostOrCut | Platô tonal versus bell localizado | Manter; ensinar curva, não fundir por ambos terem boost/cut. [DSP:363–405] |
| Compression / Ratio / Threshold / Attack / Release | Detecção versus controles distintos | Manter; não adicionar “Compression or No Compression?” como sexto alias. [CAT; EXP] |
| ReverbPresence / ReverbMix / Decay / PreDelay | Presença / quantidade / duração / separação | Manter, aproximar na trilha e explicar o eixo variável. [EXP:63–84,160–172] |
| StereoPosition / Width / ChannelPolarity | Pan / Side / inversão relativa | Manter; Mono Compatibility acrescentaria habilidade, não alias. [GEN:343–380; DSP:461–479] |
| Distortion / NoiseDetection / MixProblem | Textura / hiss / diagnóstico-ação | Manter; clipping/artefatos/mascaramento ampliariam conteúdo de verdade. [EXP:94–99,173–199] |
| FindBoost/FindCut/Brighter/Warmer/LowMidHigh | Parcialmente cobertos por dinâmicas existentes | Preferir modos didáticos/presets antes de cinco novos exercícios seedados. |

## 7. Roadmap sugerido

### 7.1 PRs por fase, dependências e aceite

| PR / fase | Escopo | Depende de | Esforço | Testes/aceite principal | Pode sair sozinha? |
|---|---|---|---|---|---|
| A — documentação sincronizada | Corrigir DOC para7 bandas/Q avançado; esclarecer categorias/defaults | Nada | S | Revisão de referências; política docs-only | Sim |
| B — categorias visíveis **(implementada)** | Aba/category badges e tipos sem seções vazias; mesmas FKs | Nada | S | INDEXTEST, UTCATALOG, TRACKTEST, E2E3 culturas/360 px | Sim |
| C — filtros combináveis | GET categoria/tipo/nível e contagem única | B, ou contrato de UX equivalente | M | URLs/presets, empty/unknown, Music sem regressão | Sim após B |
| D — validade EQ Matching | Casos-limite, decisão de `IsCorrect`, versão de regra e feedback claro | Decisão sobre pisos/sinal | S–M | EQMTEST, ITA, ITP; score antigo não reclassificado | Sim |
| E — BoostOrCut pedagógico | Pools comuns no bundled, subsets por nível, centros revisados | Aprovação de63 Hz se usado | M | UTNEW/PRIVATE, todos candidatos por fonte, reset/console/loc | Sim |
| F — tema/controles compartilhados | Extração knob/range, contraste, light/dark e números localizados | Nada; preservar UI atual | M | EQ/BC touch, teclado, aria, tutorial, viewport | Sim |
| G — primeira família de novos layouts | StereoPosition, GuessFrequency, cutoff/shelf/EQ gain | F para knob; rail/SVG podem sair antes | M | Não mudar respostas; contratos JSON e E2E | Por família |
| H — feedback técnico | Palpite/correto/pista, curvas pós-resposta; GR medido depois | F opcional; DSP stats para GR | M–L | Leitura somente após validate; replay; a11y; dados medidos | Curvas primeiro |
| I — perfis/fontes coerentes | GuessQ introdutório, ReverbMix, polarity bundled, qualification EqMatching | Decisões dos perfis | M–L | DSP/encoded/loudness, sources, escuta humana | Por exercício |
| J — analytics contínuo | Erros por banda/parâmetro e sugestões de filtro | D; persistência mínima aprovada | M | Dados antigos/null, bounds, export/deletion, relatório por track | Sim após contrato |
| K — Audio path v2 | Ordem pedagógica/níveis com preservação de v1 | Política de histórico/versionamento | L | PEVAL/PSVC, fixtures de alunos existentes; Music intacta | Não como simples reorder |
| L — novo conteúdo distinto | Mono Compatibility ou Masking primeiro | Fontes/DSP/critério do exercício | M–L | Registro completo + planner/generator/validator/path/culturas | Um exercício por PR |
| M — Mix Challenges | Um cenário com stems/ações antes de catálogo amplo | L para masking, ou novo cenário independente | L | Blindness, regras de ações válidas, render budgets, track Mix | Piloto independente |
| N — adaptação/badges adicionais | Sessões técnicas e badges de domínios ainda não cobertos pelos sete atuais | J; política de XP/níveis | L | TimeProvider, eligibility, estados antigos, arte/localização | Extensões de badges separadas |

### 7.2 Mudanças realmente independentes

- Categorias no catálogo não exigem novo DSP, níveis revisados, schema ou reordenação da trilha.
- Localização de decimais pode sair sem trocar o visual ou o algoritmo.
- Temas dos consoles já existentes podem sair sem alterar scoring.
- Feedback de EQ Matching pode usar `detail` já entregue; métricas históricas são uma PR separada.
- Correção documental não precisa esperar um novo exercício; este relatório não substitui o registro de implementação.
- ReverbMix, GuessQ e parity de polarity podem ser ajustados em PRs distintas, com perfil versionado quando mudar significado histórico.
- Não agrupar taxonomia, novo engine e path v2: são riscos e testes diferentes.
- Não usar a branch deste relatório para implementação/commit enquanto houver merges concorrentes.

### 7.3 Checklist obrigatório para qualquer novo exercício

1. **Seed:** Name estável, categoria/type/default, filtros, instruções e opções; preservar resolução de lookup por nome.
2. **Playback:** cobertura explícita do exercício. Para Music, case em `ExercisePlaybackPlanner`/geração musical; para Audio, `Supports/Plan` no gerador técnico e o roteamento existente.
3. A exigência de “case no planner” deve ser entendida como cobertura do caminho de playback: os 27 Áudio atuais bypassam o planner musical. Não inserir case morto só para cumprir checklist. [CTRL:252–255; PLANNER:303–307]
4. **Validator:** `IExerciseValidator`, registro em Program e validação de valores malformados/NaN/infinity/fora de faixa. [PROGRAM:270–279; EQV:22–47]
5. **Trilha:** passo/unidade da track correta; preservar a unicidade exigida pelas convenções e testes. [PATH; UTPATH:17–46]
6. **Culturas:** nome, subtitle, instructions/tips, pergunta, alternativas, feedback e Level.* nas três resx; não confiar no fallback português do seed.
7. **Contrato privado:** resposta RequestPlay só com opções/prompt seguros e tokens; expected answer, fonte e receita ficam no servidor.
8. **Staff de Music:** manter whitelist de metadata, nunca notas/respostas completas de ditados. A pauta mostrada para cantar em SolfegeMelody tem semântica diferente de resposta oculta. [CTRL:338–443]
9. **POST:** incluir explicitamente `[ValidateAntiForgeryToken]` em toda action nova conforme requisito do projeto/CodeQL; os filtros automáticos existentes não dispensam esse cuidado editorial.
10. O runtime já tem `AutoValidateAntiforgeryToken` global e injeção do header no fetch; não alegar que os POST atuais estão sem proteção por falta de annotation individual. [PROGRAM:335–344; LAYOUT:195–218]
11. **Tempo:** usar `TimeProvider` injetado, particularmente scoring, expiração, adaptação e agenda; não pegar hora do cliente. [CTRL:47,590–592; TOKEN:21–26,69; DSVC:23–37]
12. **Layout:** gutters ≤gx-4 abaixo de xl, 360 px sem scroll horizontal, foco/teclado/toque e light/dark.
13. **DSP:** processor compartilhado, limites e plano hash/versionados; bump `EngineVersion` quando o mesmo plano mudar o PCM, não só por trocar CSS. [DSP:13–17,303–326]
14. **Fontes:** licença, metadata/hash, energia útil e qualificação por alternativa; nenhuma URL de master/recipe enviada ao browser.
15. **Engajamento:** decidir DailyChallenge, badges, games/placement e relatório; não ampliar requisitos globais legados por acidente.
16. **Arte de badges:** seguir `scripts\export-badge-art.py`; medal WebP192 px, fonte PNG transparente fora do repo, disponibilidade só após aprovação/regra/textos. [ART:1–20,81–87; BADGE:62–100]

**CodeQL:** o workflow cobre C# e JavaScript/TypeScript; não foi executado nesta revisão.
A exigência de annotation explícita é mantida como requisito informado; não foi confirmado aqui o comportamento de uma query específica sobre cada POST. [CODEQL:20–26,54–67]

### 7.4 Impacto de testes por tipo de PR

| Mudança | Menor cobertura recomendada antes de ampliar para CI |
|---|---|
| Somente este relatório/documentação | Verificação estrutural/referências; docs-only segue CONVENT:74–80 |
| Categoria/URL do catálogo | INDEXTEST, UTCATALOG, UTSEED, TRACKTEST; E2E catalog + culturas + viewports |
| UI sem alteração de resposta | ITA/pages, TUTOR, E2E teclado/touch/reset/replay/erro; assert de keys/ausência de alvo |
| Perfil de nível | UTNEW/UTEXP/UTNEXT ou testes da Foundation; alternativas × fontes × níveis; recursos e filtros |
| Pontuação EQ Matching | EQMTEST + ITA; tolerâncias/bounds/80%/sinal/zero; score binário e contínuo separados |
| DSP/preview | DSP numérico, matching/headroom, WAV encoded, cache version; equivalência/plausibilidade RMS/LUFS |
| Fontes privadas | PRIVATE/PRIVATEBASE opt-in com assets autorizados; não copiar WAV privado para repo |
| Progressão/trilha | UTPATH + PEVAL/PSVC fixtures; preservação histórica; Music/placement/badges |
| Analytics/persistência | ITP, migrations se schema mudar, export/deletion, filtros antigos/null, consultas por track |
| Novo exercício | Seed/catálogo/validator/playback/path/nome nas3 culturas; tutorial e E2E que percorrem exercícios |

Funcionalidade exige a CI completa definida em CONTRIBUTING; testes seletivos não substituem SQL/Linux DSP/Playwright/CodeQL/scans. [CONVENT:60–105] Esta revisão documental não executou builds/tests.

### 7.5 Métricas para decidir se o incremento valeu a pena

- **Descoberta:** início de treino por categoria; necessidade de rolar os81 cards por nível versus27 exercícios únicos. O número81 decorre do agrupamento atual, não de81 exercícios. [INDEX:77–136]
- **Pedagogia:** matriz de confusão entre bandas/valores adjacentes, não só accuracy global.
- **Progressão:** taxa de acerto B/I/A por material; diferença relevante entre perfis confirmada por escuta.
- **Continuidade:** erro F em cents, G em dB, Q em razão; mostrar percentil/mediana só com amostras suficientes.
- **UX:** sucesso com teclado/toque, tempo até primeira reprodução e taxa de reset/erro.
- **Operação:** tempo de render e bytes transferidos; MixProblem já pede sete clips, e ABJS atualmente preloads todos. [EXP:208–215; ABJS:99–116]
- **Proteção:** nenhuma nova chave de alvo em RequestPlay/DOM antes da validação; replay não revalida nem gera reward extra.

## 8. Riscos e perguntas em aberto

### 8.1 Decisões que o responsável pelo produto deve tomar

| Pergunta | Recomendação inicial | Consequência da alternativa |
|---|---|---|
| Concluir a Audio path significa conhecer os fenômenos ou dominar Advanced? | Nomear como percurso introdutório; domínio avançado separado | Exigir A altera duração, fixtures e progressão histórica. |
| Score90% com ganho zero em EQ Matching pode ser “correto”? | Manter precisão parcial, mas exigir direção/piso de cada componente | Aceitar regra atual deve ser uma decisão consciente, explicada no feedback. |
| Q introdutório deve generalizar frequência já no primeiro nível? | Centro público fixo no B; variabilidade depois | Manter centro oculto exige reconhecer a carga adicional. |
| Boost or Cut deve ter todas as sete bandas desde B? | Subsets por perfil; sete no A | Manter sete requer fontes/centros que sustentem discriminação fácil. |
| MixProblem mede diagnóstico puro ou decisão com audition? | Explicitar decisão com audition; duas etapas se quiser medir diagnóstico separado | Audition antes do diagnóstico pode revelar a solução por comparação; isso é dinâmica, não fuga de metadata. |
| “Dry” significa gravação sem reverb ou nenhum reverb adicionado? | Sempre a segunda acepção nos exercícios atuais | A primeira exige fontes realmente secas/stems. |
| Presets de trilha passam a ser requisitos de nível? | Não neste incremento; planejar v2 com versionamento | Alterar evaluator muda reconhecimento de histórico e progresso existente. |
| Vale migrar tipos para MC/continuous/diagnostic agora? | Não; descritor/tag code-first primeiro | Trocar type IDs impacta dashboard/rotinas/seed sem resolver o domínio sozinho. |
| Uma nova fonte dedicada vale mais que outro exercício de EQ? | Sim para vocal/masking/temporal | Novos exercícios sobre o mesmo full-mix não demonstram generalização. |
| Speed/streak/difficulty devem aumentar XP? | Primeiro medir e decidir; não incentivar responder antes de ouvir | Regras globais e perfil histórico podem mudar; distinguir streak de prática e de acertos. |
| Pode haver várias correções de mix tecnicamente válidas? | Sim em futuros cenários, com rubrica explícita | Validator de resposta única do exercício atual não representa julgamento artístico geral. |
| Painel escuro fixo dentro de tema claro é escolha visual ou requisito não atendido? | Oferecer variantes/readability test | Manter dark “hardware” deve ser decisão explícita, não afirmar que o painel já é themed. |

### 8.2 Principais riscos de implementação

- **Vazamento pedagógico:** meters, correlação, espectro e curvas podem responder à pergunta mesmo sem JSON “answer”.
- **Histórico:** categorias/pools de desafio/trilha são derivados; alterar chaves ou ordem pode mudar estatísticas e conclusões passadas.
- **Contrato contínuo:** peso médio alto pode mascarar parâmetro incompatível; separar precisão exibida e critério de aprovação.
- **Fidelidade:** GR requer medição do detector, decay nominal não é RT60, preview RMS não é canonical BS.1770.
- **Assets:** fontes privadas/qualificação auditiva não são equivalentes a seis clips sintetizados no local; manter licença e paridade de objetivo.
- **Mobile:** sete reparos, knobs e labels extensos podem elevar tráfego e altura; horizontal scroll não é o único indicador de usabilidade.
- **Performance:** mais clips, novas caudas e GR timelines têm custo; conservar limites de duração, stages e cache.
- **Não duplicar propósito:** alias de “Find the Boost” ou “Guess Ratio” não deve aumentar a contagem sem uma habilidade/dinâmica nova.
- **Localização:** labels pequenos, decimal “.” e strings de fallback podem passar em teste de existência de key e continuar inadequados.
- **Escopo:** introduzir categorias + redesign + novo DSP + path v2 em um só PR dificulta atribuir regressões.

### 8.3 Fatos que esta revisão não pode confirmar

1. Qual commit está em produção e quais PRs concorrentes estarão incorporados depois deste checkout.
2. Resultado atual de build, unit/integration/e2e, CodeQL ou testes de assets privados: foram apenas lidos.
3. Se os clips privados autorizados estavam disponíveis/qualificados na instância de produção; os testes opt-in dependem de `AA_TEST_TRAINING_SOURCES`. [PRIVATEBASE:173–176]
4. Audibilidade, conforto, preferência de layout e dificuldade relativa percebida por pessoas; não houve escuta.
5. Contraste real renderizado, qualidade de screen reader e comportamento de browser/device além do que os testes existentes declaram.
6. Aprovação de licenças/novos stems e se gravações privadas contêm vocal, fala, guitarra ou piano isoláveis; metadata atual chama-as de `mix`.
7. JND individual/equipamento do aluno; nenhum perfil deve ser tratado como avaliação audiológica.
8. A query exata de CodeQL que motive annotation explícita em todos os POST; manter a convenção sem inferir vulnerabilidade dos filtros automáticos atuais.

**Critério de próxima decisão:** priorizar uma PR de categorias e uma de validade/feedback; validar com escuta antes de expandir novamente o catálogo.

## Apêndice: índice de arquivos e linhas

Referências são do checkout consultado e podem deslocar com merges. **Caminhos relativos a `F:\repos\AcademiaAuditiva`, exceto DESIGN.**

| ID | Arquivo / linhas | Uso neste relatório |
|---|---|---|
| CAT | `AcademiaAuditiva\Services\Audio\AudioComparisonCatalog.cs:1–51` | 24 definições,20 Expansion,1 Continuous, filtros, respostas, referência A |
| GEN | `AcademiaAuditiva\Services\Audio\TechnicalListeningRoundGenerator.cs:25–471` | Profiles Foundations, BC/Q/compression/matching, elegibilidade, respostas |
| EXP | `AcademiaAuditiva\Services\Audio\TechnicalListeningRoundGenerator.Expansion.cs:9–250` | Parâmetros dos20 exercícios, randomização, repairs, pools |
| SEED | `AcademiaAuditiva\Data\SeedData.cs:9–105,1590–1732` | Lookups/categorias/tipos, remapeamento de IDs, defaults e filtros Áudio |
| PATH | `AcademiaAuditiva\Services\LearningPath\LearningPathCatalog.cs:1–105` | Ordem27 passos, beginner, janela10/meta7 e tracks |
| TRACK | `AcademiaAuditiva\Services\TrainingTracks.cs:3–64` | Track derivada, seis categorias Audio, Mix vazio, filtro SQL |
| CATALOG | `AcademiaAuditiva\Services\ExerciseCatalog.cs:1–70` | Catálogo code-first, agrupamento category/track, dificuldade default |
| MODEL | `AcademiaAuditiva\Models\Exercise.cs:7–54`; `ExerciseCategory.cs:5–14`; `ExerciseType.cs:5–14` no mesmo diretório | FKs, Name/DisplayName, FiltersJson |
| CTRL | `AcademiaAuditiva\Controllers\ExerciseController.cs:29–47,101–131,198–335,338–458,463–640,703–785,843–879,1093–1159` | Catálogo, routes, geração técnica, payload privado, validação, tempo, reveal |
| PLANNER | `AcademiaAuditiva\Services\Audio\ExercisePlaybackPlanner.cs:135–166,303–307` | Caminho musical/case e falha para nome desconhecido |
| VAL | `AcademiaAuditiva\Services\ExerciseValidators\ExerciseValidators.cs:13–24,170–299` | Validadores discretos de Foundations/BC/Q/compression |
| EV | `AcademiaAuditiva\Services\ExerciseValidators\ExpandedAudioExerciseValidator.cs:7–30` | Resposta exata e parâmetros após validação |
| EQV | `AcademiaAuditiva\Services\ExerciseValidators\EqMatchingValidator.cs:6–56` | Pesos, tolerâncias, score>80%, bounds e detail |
| VIEW | `AcademiaAuditiva\Views\Exercise\AudioComparison.cshtml:1–143` | UI comum, ramo BoostOrCut, GuessQ, repairs e localizer |
| JS | `AcademiaAuditiva\wwwroot\js\Exercises\AudioComparison.js:1–134` | Composição BC, reset, levels, feedback, replay preservado |
| LM | `AcademiaAuditiva\Views\Exercise\LevelMatch.cshtml:1–66` | A/B, lado e diferença discreta |
| LMJS | `AcademiaAuditiva\wwwroot\js\Exercises\LevelMatch.js:1–129` | Seleção, troca de nível, validação/feedback |
| SP | `AcademiaAuditiva\Views\Exercise\StereoPosition.cshtml:1–82` | Posições discretas e aviso do dispositivo |
| SPJS | `AcademiaAuditiva\wwwroot\js\Exercises\StereoPosition.js:1–94` | Visibilidade por perfil e seleção |
| GF | `AcademiaAuditiva\Views\Exercise\GuessFrequency.cshtml:1–68` | Frequências e contextos localizados |
| GFJS | `AcademiaAuditiva\wwwroot\js\Exercises\GuessFrequency.js:1–99` | Quatro opções sorteadas, feedback e reset |
| EQ | `AcademiaAuditiva\Views\Exercise\EqMatching.cshtml:1–127` | Strip, ranges/knobs, curva do palpite, preview, tutorial |
| EQJS | `AcademiaAuditiva\wwwroot\js\Exercises\EqMatching.js:1–197` | Curva44100, pointer/keyboard, labels, preview, score |
| BCSS | `AcademiaAuditiva\wwwroot\css\boost-cut-console.css:1–199` | Paleta própria, seleção/foco,7→4→2 colunas |
| EQCSS | `AcademiaAuditiva\wwwroot\css\eq-channel-strip.css:1–216` | Dark fixo, knob/range,3 controles e textos mobile |
| CSS | `AcademiaAuditiva\wwwroot\css\aa-design-system.css:42–145,1903–1971,2047–2099,3656–3667` | Temas comuns, A/B flex wrap, respostas, reduced-motion |
| AB | `AcademiaAuditiva\Views\Exercise\_ABPlayback.cshtml:1–69` | Botões neutros, single, repairs, shortcuts/live region |
| ABJS | `AcademiaAuditiva\wwwroot\js\core\ab-playback.js:1–177` | Preload/troca/replay/readiness/teclado |
| ENGINE | `AcademiaAuditiva\wwwroot\js\core\audio-engine.js:31–47,91–193,229–305` | Contexto, buffers sincronizados, preview RMS/peak margin |
| DSPPLAN | `AcademiaAuditiva\Services\Audio\Processing\AudioProcessingPlan.cs:1–112` | Processors existentes e semântica |
| DSP | `AcademiaAuditiva\Services\Audio\Processing\AudioProcessing.cs:13–45,49–220,303–326,363–534` | Limites/cache, filters, compressor, width/polarity/noise/saturação/matching |
| TIME | `AcademiaAuditiva\Services\Audio\Processing\TimeBasedAudioProcessing.cs:5–96` | Echo/reverb, pre-delay e damping, duração preservada |
| SOURCES | `AcademiaAuditiva\Audio\Sources\sources.json:1–209` | Seis assets bundled, tipos, tags, usos/dificuldades e medidas |
| LIB | `AcademiaAuditiva\Services\Audio\Sources\AudioSourceLibrary.cs:198–268` | Chunks privados, kindmix, filtros de elegibilidade |
| TOKEN | `AcademiaAuditiva\Services\Audio\AudioTokenService.cs:10–99` | Rodadas e identificadores opacos, estado privado, TTL e TimeProvider |
| FILTER | `AcademiaAuditiva\Services\ExerciseFilterPresets.cs:23–144` | Defaults, sanitização de presets e serialização |
| FVIEW | `AcademiaAuditiva\Views\Exercise\_ExerciseFilters.cshtml:67–101` | Query pré-selecionada e locks da rotina |
| INDEX | `AcademiaAuditiva\Views\Exercise\Index.cshtml:1–136` | Track tabs, níveis declarados, agrupamentos nível/tipo |
| REPORT | `AcademiaAuditiva\Services\UserReportService.cs:12–147` | Radar/types, category, relatórios por track, nível jogado |
| DCTRL | `AcademiaAuditiva\Controllers\DashboardController.cs:49–104,172–201` | Dashboard track-aware e endpoints de relatórios |
| DASH | `AcademiaAuditiva\Views\Dashboard\Index.cshtml:38–54,270–300` | Tracks, mapas localizados, um tipo de Áudio e categorias |
| DSTATS | `AcademiaAuditiva\Services\Dashboard\DashboardStats.cs:47–74,145–184` | Recomendação por exercício, janela20/min5 e limiares70/90 |
| DRULE | `AcademiaAuditiva\Services\DailyChallenge\DailyChallengeRules.cs:32–63,75–94,122–185` | Três exercícios,5 respostas, tracks e rotação por categoria |
| DSVC | `AcademiaAuditiva\Services\DailyChallenge\DailyChallengeService.cs:20–47` | Pool, histórico e dia por TimeProvider |
| PEVAL | `AcademiaAuditiva\Services\LearningPath\LearningPathEvaluator.cs:28–93` | Avaliação cronológica/threshold, sem filtro por perfil |
| PSVC | `AcademiaAuditiva\Services\LearningPath\LearningPathService.cs:75–164` | Progresso recomputado, presets e placement Music |
| SNAP | `AcademiaAuditiva\Models\ScoreSnapshot.cs:10–76` | Boolean/time/filter256, aggregates, ausência de score contínuo tipado |
| ANALYTICS | `AcademiaAuditiva\Services\AnalyticsService.cs:15–116` | Logs best-effort, modo no-op e ausência de garantia de persistência |
| SCORE | `AcademiaAuditiva\Services\Scoring\ScoreAggregator.cs:19–29` | Contagem correta/erro e BestScore |
| CLOCK | `AcademiaAuditiva\Services\Scoring\AnswerTime.cs:10–30` | Tempo medido no servidor, cap300s |
| XP | `AcademiaAuditiva\Services\Gamification\PlayerProgress.cs:3–102` | XP10/2/50, ranks/nível e streak |
| SESSIONS | `AcademiaAuditiva\Services\Gamification\PracticeSessions.cs:21–49` | Sessão contada:30 min de gap máximo e5 respostas mínimas |
| BADGE | `AcademiaAuditiva\Services\Gamification\BadgeCatalog.cs:13–119` | Sete badges Audio disponíveis e ausência de aliases/domínios adicionais |
| BRULE | `AcademiaAuditiva\Services\Gamification\BadgeRules.cs:83–91,173–187,300–309,345–378` | Regras Audio e pools legados Music-only |
| GAMES | `AcademiaAuditiva\Services\Games\GameModes.cs:38–43` | Games/placement/weak-spots não incluem Áudio |
| LOC | `AcademiaAuditiva\Resources\SharedResources.resx:594–689,3859–3900,4921–5560`; `SharedResources.pt-BR.resx` e `SharedResources.fr-CA.resx`, mesmas keys, bloco Áudio até5556 | Ranges do corpo apontam ao base en-US; pt/fr têm quatro linhas a menos a partir de GuessFrequency |
| PROGRAM | `AcademiaAuditiva\Program.cs:270–279,303,335–344` | DI de validators, TimeProvider e anti-forgery global |
| LAYOUT | `AcademiaAuditiva\Views\Shared\_Layout.cshtml:195–218,236–247` | Header anti-forgery e scripts compartilhados |
| UTNEW | `Tests\AcademiaAuditiva.UnitTests\NewAudioExerciseTests.cs:15–141` | BC/Q/Compression: rounds, source/DSP, recursos |
| UTEXP | `Tests\AcademiaAuditiva.UnitTests\ExpandedAudioExerciseTests.cs:14–142` |20 exercícios, níveis, repairs e localização |
| UTNEXT | `Tests\AcademiaAuditiva.UnitTests\NextAudioExerciseTests.cs:17–108` | Contrastes, tipos e escolhas do terceiro batch |
| EQMTEST | `Tests\AcademiaAuditiva.UnitTests\EqMatchingExerciseTests.cs:14–52` | Alvo privado, score e invalidação de números |
| UTSEED | `Tests\AcademiaAuditiva.UnitTests\AudioTrackSeedTests.cs:9–57` | Append de categorias, banco existente e IDs por nome |
| UTCATALOG | `Tests\AcademiaAuditiva.UnitTests\ExerciseCatalogTests.cs:13–61` | Catálogo/seed, unicidade, ordem por track/categoria |
| TRACKTEST | `Tests\AcademiaAuditiva.UnitTests\TrainingTracksTests.cs:16–97` | Track conhecida e textos por cultura |
| UTPATH | `Tests\AcademiaAuditiva.UnitTests\LearningPathServiceTests.cs:17–110` | Presets,27 passos e isolamento Music/Audio |
| PRIVATE | `Tests\AcademiaAuditiva.UnitTests\PrivateExpandedAudioTests.cs:10–179` | Qualificação opt-in de energia Side, ratios, tonal/noise |
| PRIVATEBASE | `Tests\AcademiaAuditiva.UnitTests\PrivateTrainingSourcesTests.cs:14–180` |96 chunks, faixas, compressão, variável de ambiente e opção privada |
| ITA | `Tests\AcademiaAuditiva.IntegrationTests\NewAudioExerciseTests.cs:27–223` | Todas as páginas/rounds,3 culturas, alvo oculto, single-use/free |
| ITP | `Tests\AcademiaAuditiva.IntegrationTests\AudioProgressTests.cs:42–173` | Dashboard/category/difficulty/recommendations com track isolada |
| INDEXTEST | `Tests\AcademiaAuditiva.IntegrationTests\ExerciseIndexTests.cs:28–162` | Níveis de cada exercício, URLs, filtros declarados e Music |
| TUTOR | `Tests\AcademiaAuditiva.IntegrationTests\TutorialTests.cs:61–77` | Tour percorre cada página seedada e seus targets |
| E2E | `Tests\e2e\tests\academia-auditiva.spec.ts:535–638,642–877,2050–2059` |27 exercícios, culturas,360 px, teclado,consoles,touch,replay e JSON |
| DOC | `docs\Audio-Ear-Training.md:1–1106` | Registro completo; contagem, arquitetura, slices, limites e roadmap |
| BACKLOG | `docs\Backlog.md:1–381`, especialmente218–262,317–336 | Convenções de exercícios, roadmap separado e badges |
| DESIGN | `C:\Users\lramoscostah\Downloads\README-Audio-Ear-Training.md:1–945` | Documento do usuário; origem da matriz de rastreabilidade |
| CONVENT | `CONTRIBUTING.md:39–105` | Localização, migrations e CI por impacto |
| CODEQL | `.github\workflows\codeql.yml:20–67` | Languages C#/JS-TS e análise aplicável |
| ART | `scripts\export-badge-art.py:1–20,81–87` | Convenções/exports de medalhas |

### Referências externas e natureza das conclusões

- **PSY1:** OpenStax, *College Physics 2e*,17.6 “Hearing”: https://openstax.org/books/college-physics-2e/pages/17-6-hearing — consultado em12/10/2026; sustenta a referência aproximada de1 dB em frequência fixa e dependência da percepção de intensidade/frequência. Não sustenta um limiar universal para EQ/compressão em música.
- **PSY2:** ITU-R BS.1116-3 (02/2015): https://www.itu.int/rec/R-REC-BS.1116-3-201502-I/en — registro consultado; referência para planejar futuras avaliações subjetivas, não certificação dos exercícios nem leitura integral da norma.
- Comparações de dificuldade, centros propostos, tolerâncias futuras, prioridades e esforço são avaliações/recomendações deste relatório.
- Percentuais de score e gaps de ReverbMix citados como derivação foram recalculados estaticamente a partir das fórmulas/conjuntos encontrados; não são medições auditivas.
