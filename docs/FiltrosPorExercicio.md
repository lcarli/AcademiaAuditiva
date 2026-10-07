
# Exercícios e Filtros Aplicáveis — Módulo Musical

Este documento detalha os filtros possíveis para cada exercício do módulo de música na plataforma Academia Auditiva.

---

## Tabela de Filtros

- **Tons Disponíveis**: C, C#, D, D#, E, F, F#, G, G#, A, A#, B
- **Escalas**: maior, menor, pentatônica, modos gregos
- **Intervalos**: 2ª, 3ª, 4ª, 5ª, 6ª, 7ª, 8ª
- **Qualidade dos Acordes**: maior, menor, diminuto, aumentado, sus2, sus4, 7, 9, etc.
- **Modo de Reprodução**: harmônico (conjunto), melódico (sequência), aleatório
- **Oitava ou faixa**: C3–C4, C4–C5, teclado todo

---

## Exercícios e Filtros

### 1. Adivinhe a Nota
- **Filtros aplicáveis**:
  - Oitava (ex: C4–C5, ou teclado inteiro)
  - Escala (maior, menor, personalizada)
  - Tom base

---

### 2. Mais alto ou mais grave
- **Filtros aplicáveis**:
  - Faixa de notas (controle global de tessitura)

---

### 3. Adivinhe o Intervalo (simples)
- **Filtros aplicáveis**:
  - Oitava da nota base
  - Intervalos permitidos (ex: só 2ª e 3ª)
  - Modo de reprodução (melódico/harmônico)

---

### 4. Intervalo Completo
- **Filtros aplicáveis**:
  - Tipos de intervalo (menor, maior, justa, etc.)
  - Oitava base
  - Direção (ascendente/descendente)

---

### 5. Adivinhe o Acorde
- **Filtros aplicáveis**:
  - Qualidades dos acordes (maior, menor, dim, aug, etc.)
  - Oitava ou faixa
  - Tipo de acorde (tríade, tétrade, add)

---

### 6. Adivinhe a Qualidade
- **Filtros aplicáveis**:
  - Tom base
  - Modo de reprodução
  - Tipos de qualidade (maior, menor, diminuto...)

---

### 7. Adivinhe a Função Harmônica
- **Filtros aplicáveis**:
  - Tom base
  - Campo harmônico maior ou menor
  - Grau alvo (I, IV, V…)

---

### 8. Adivinhe a inversão
- **Filtros aplicáveis**:
  - Qualidade do acorde (maior, menor ou ambas)
  - Oitava base

---

### 9. Adivinhe a cadência
- **Filtros aplicáveis**:
  - Tom base
  - Campo harmônico maior ou menor

---

### 10. Complete o acorde
O aluno ouve o acorde inteiro (tocado como está escrito) e escreve as notas empilhadas na pauta, em posição fundamental.
- **Filtros aplicáveis**:
  - Qualidade do acorde: maiores, menores, maiores e menores, todas as tríades (maior, menor, diminuta, aumentada), acordes com sétima (7M, 7, m7, m7(b5), dim7) ou todos
  - Acidentes: só acordes sem acidentes ou com acidentes
  - Fundamental: dada na pauta (o aluno completa o resto) ou oculta (escreve o acorde todo)
  - Oitava da fundamental: 4 (clave de sol) ou 3 (clave de fá)

---

### 11. Grau Inicial e Final
- **Filtros aplicáveis**:
  - Tom da melodia
  - Comprimento da melodia (ex: 5 a 10 notas)
  - Escala (maior/menor/modo)

---

### 12. Dictado Melódico
- **Filtros aplicáveis**:
  - Tom base
  - Escala usada
  - Comprimento da melodia
  - Presença de cromatismos
  - Nível de dificuldade
  - Número de compassos

---

### 13. Dictado Rítmico
- **Filtros aplicáveis**:
  - Compasso (2/4, 3/4, 6/8…)
  - Figuras rítmicas incluídas
  - Duração da célula
  - Nível de dificuldade
  - Número de compassos

---

### 14. Reproduza o Intervalo
- **Filtros aplicáveis**:
  - Intervalos sorteáveis
  - Direção do intervalo
  - Tom base

---

### 15. Note Missing / Note Modified
- **Filtros aplicáveis**:
  - Número de notas da melodia
  - Escala usada
  - Tom base
  - Tipo de alteração (nota faltante, nota errada, etc.)

---

### 16. Igual ou Diferente
- **Filtros aplicáveis**:
  - Tipo de material (notas, acordes, melodias)
  - Escala
  - Probabilidade de mudança

---

### 17. Adivinhe a Escala
- **Filtros aplicáveis**:
  - Tipos de escala (maior, menor, modos gregos)
  - Tom base
  - Número de notas a serem tocadas

---

### 18. Adivinhe o Modo Grego
- **Filtros aplicáveis**:
  - Modos possíveis (jónico, dórico, frígio, etc.)
  - Tom base
  - Duração da amostra

---

### 19. Complete a escala
- **Filtros aplicáveis**:
  - Tônica
  - Tipo de escala (maior, menor, pentatônica maior ou menor)
  - Oitava base

---

### 20. Transponha a escala
- **Filtros aplicáveis**:
  - Tônica original
  - Escala maior ou menor
  - Tonalidade-alvo sorteada pelo sistema

---

### 21. Adivinhe o grau
O aluno ouve uma cadência que estabelece a tonalidade (I–IV–V–I, ou i–iv–V–i na menor) e, depois, uma nota, e diz qual é o grau dela na tonalidade.
- **Filtros aplicáveis**:
  - Tom base, ou qualquer um (sorteado a cada questão)
  - Tonalidade maior ou menor (na menor, os graus são contados na escala menor natural)
  - Nível: diatônico (graus 1 a 7) ou cromático (as 12 notas, com os graus alterados ♭ e ♯)
  - Faixa de oitavas (a oitava da tonalidade)

---

## Observações

- Filtros devem ser opcionais e salvos por sessão de usuário
- As combinações de filtros podem alterar a dificuldade automaticamente
- Jogos podem desabilitar ou randomizar filtros
- A **faixa de oitavas** (controle global de tessitura) só aparece nos exercícios cujas notas saem dela: Adivinhe a Nota, Mais alto ou mais grave, Adivinhe o Grau, Adivinhe o Acorde, a Qualidade, a Função Harmônica, a Inversão e a Cadência (`MusicTheoryService.UsesNoteRange`). Os demais fixam a oitava ou têm um filtro de oitava próprio (ex.: Complete o acorde)
