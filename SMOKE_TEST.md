# Smoke Test Manual — `feat/v2-layout-polish`

App: http://localhost:5000
Admin: `lucas.decarli.ca@gmail.com` / `Admin!LocalDev1`

> Marque `[x]` no que passa, anote `❌ <descrição>` no que falha.

---

## 0. Anti-cheat de áudio (NEW)

Para os 7 exercícios em escopo: **GuessNote, GuessChords, GuessFunction,
GuessInterval, GuessFullInterval, GuessQuality, GuessMissingNote**.

DevTools → **Network**, filtrar por `audio` ou por `Exercise`:

- [ ] `POST /Exercise/RequestPlay` na resposta tem **somente** `roundId` + `playToken` (ou `melody1Token` + `melody2Token` para GuessMissingNote). Nenhum dos campos a seguir aparece: `note`, `notes`, `note1`, `note2`, `root`, `quality`, `type`, `melody1`, `melody2`, `answer`.
- [ ] Não existe nenhum request a `/audio/C4.mp3`, `/audio/Cs4.mp3`, etc. — apenas `/audio/token/<guid>`.
- [ ] A URL em `/audio/token/<guid>` é um GUID hex de 32 caracteres (não um nome de nota).
- [ ] `Replay` re-toca sem novo download (buffer cacheado em memória pelo `AudioEngine`).

DevTools → **Console**:

- [ ] `window.currentChordNotes` é `undefined`.
- [ ] No estado de "esperando resposta" não existe nenhuma variável global ou local exposta com o nome da nota / acorde / intervalo.

DevTools → **Application/Storage**:

- [ ] Nenhum cache HTTP guarda o áudio do round (`Cache-Control: no-store` no response de `/audio/token/...`).

Após **Validate**:

- [ ] Resposta JSON inclui `answer` correto — pedagógico, esperado.
- [ ] Mesmo `roundId` enviado de novo → `success:false` com `Exercise.SessionExpired` (round consumido).

Sheet-music exercises (fora de escopo, devem continuar funcionando):

- [ ] **IntervalMelodico** ainda toca via Sampler legado (notas conhecidas pelo browser por design).
- [ ] **SolfegeMelody** ainda renderiza partitura corretamente.

## 1. Idioma & Tema (header)
- [X] Switcher de idioma no header alterna **EN / FR-CA / PT-BR** e persiste após reload
- [X] Toggle dark/light no header alterna tema e persiste após reload
- [ ] Em cada idioma, navegação principal (Home / Dashboard / Games / Exercises / Login) está traduzida ❌

## 2. Landing (não-autenticado)
- [X] Hero, seções e footer em PT/EN/FR
- [X] Footer colado embaixo (não fica no meio da tela em viewport grande)
- [X] Dark mode aplica nas cores de hero/cards

## 3. Auth
- [X] Login com admin acima → redireciona OK
- [X] Header mostra **"Olá, Lucas"** (não o email)
- [ ] Logout volta pra landing ❌

## 4. Dashboard (`/Dashboard`)
- [X] Greeting traduzido por idioma
- [X] **Radar — Skills**: labels (NoteRecognition, ChordRecognition…) traduzidos
- [X] **Radar — Categories**: labels (EarTraining, Harmony, Melody…) traduzidos
- [X] Tabela "Recent activity": coluna *Exercise* mostra nome traduzido (não `GuessNote`)
- [ ] Lista "Pontos fracos / Weak spots": exercícios traduzidos ❌
- [X] Footer colado embaixo após os charts

## 5. Exercícios (`/Exercise/<...>`)
Teste em cada idioma (EN/FR/PT) ao menos 3 exercícios diferentes:

- [X] **GuessNote** — instruções + 3 tips traduzidos
- [X] **GuessChords** — instruções + tips traduzidos
- [X] **GuessInterval** — instruções + tips traduzidos
- [X] **GuessQuality** — instruções + tips traduzidos
- [X] **GuessFunction** — instruções + tips traduzidos
- [X] **GuessFullInterval** — instruções + tips traduzidos
- [X] **GuessMissingNote** — instruções + tips traduzidos
- [X] **IntervalMelodico** — instruções + 4 tips traduzidos
- [X] **SolfegeMelody** — instruções + tips traduzidos
- [X] Footer colado embaixo na tela do exercício
- [X] Voltar pra `/MyTraining` → footer continua colado

## 6. Teacher Area (`/Teacher`)
> Como admin (admin tem role Teacher? Se não, pular ou logar com conta teacher).

- [X] **Classrooms / Index** sem turmas: empty state traduzido + botão "Create first" traduzido
- [X] Botão **"+ New classroom"** traduzido em cada idioma
- [ ] Criar 1 classroom → tabela mostra colunas (Name / Members / Pending invites / Created) traduzidas ❌
- [X] Ações Edit / Archive / Restore traduzidas
- [X] **Routines / Index** sem rotinas: empty state + botão **"+ New routine"** traduzido
- [ ] Criar 1 routine → tabela com Name / Items / Assignments / ações traduzidas ❌

## 7. Admin Area (`/Admin`)
- [X] Sidebar: Dashboard / Users / Exercises / Games funcionam (sem 404)
- [X] Cada subpágina renderiza sem `Uncaught SyntaxError` no console

## 8. Console & rede
Em cada página visitada, abrir DevTools:

- [X] Sem `SyntaxError: Identifier 'X' has already been declared`
- [X] Sem 404 em assets (`/Identity/Account/Login`, scripts, etc.)
- [X] Charts.js / htmx carregam sem erro

---

## ⚠️ Bugs conhecidos / fora de escopo

- Subviews Teacher (Form, Details, Members/Invite, Routines/Form, Routines/Assign, Routines/Details, Dashboard/Classroom, Dashboard/Student) **ainda têm strings inglesas hardcoded**.
- Botões de resposta em alguns exercícios (`Iguais`/`Diferentes`, `Correto`/`Errado`) ainda em PT (vêm do seed do DB).

---

## Notas / falhas encontradas

<!-- escreva aqui o que precisar -->

### Pontos de melhoria
- Em admin, gostaria de poder bloquear/desbloquear um usuario ou deletar um usuario.
- Fazer um email HTML bonito para confirmacao de email quando autenticado. Criar o template para outros casos, como recurperacao de senha, etc.

### Erros
- Em admin, usuarios, o botao filtro, make a teacher, make admin e remove admin estao sempre em ingles. 
- Na hora de criar um novo usuario, os placeholders FirstName e LastName estao em ingles.
- Toda a parte de gestao do usuario (a propria conta, Identity/Account/Manage) estao sem layout moderno e com tudo em ingles. Colocar um layout consistente com o site e colocar as traducoes. \
- Logout volta para a tela de login, e nao para a landing page

### Erros com imagens:
- Erro1 : Em frances e em portugues, quando tem acentos graficos, os nomes das categorias aparecem com erro.
- Erro2: Em recomendacoes e maiores erros, veja quem em frances ou ingles, o texto ta sempre em portugues
- Erro3: Nos exercicios, em ingles ou frances, o "subtitulo" aparece em portugues (Devinez la Note, aparece abaixo: Advinhe a nota tocada)
- Erro4: O layout tem erros. O toasted verde nao some (classroom created.) e esta repetido. Ele nao estas traduzido. O pending Invites parece bizarro, sem margens. Layout ruim. A lista de membros tb. O Invite Student esta em ingles, e todo o processo tb esta em ingles (a mensagem e todo o resto). Rotina tb esta assim! Mesmos erros!