| Categoria              | Insight                                                                 | Já existe no UserReportService? |
|------------------------|------------------------------------------------------------------------|----------------------------------|
| Comportamento          | Sequência de dias consecutivos praticando                             | ✅ `GetConsecutivePracticeStreak` |
| Comportamento          | Dias sem praticar (gaps)                                               | ❌                                |
| Comportamento          | Tendência de engajamento                                               | ❌                                |
| Comportamento          | Variedade de exercícios (diversidade)                                 | ✅ `GetExerciseVarietyRatio`      |
| Comportamento          | Padrões de repetição                                                   | ❌                                |
| Comportamento          | Mudança de dificuldade ao longo do tempo                              | ❌                                |
| Comportamento          | Novos exercícios tentados por semana                                  | ❌                                |
| Comportamento          | Primeiro e último exercício feito                                     | ❌                                |
| Erros e Acertos        | Respostas incorretas mais frequentes                                  | ✅ `GetMostCommonMistakes`        |
| Erros e Acertos        | Notas/acordes mais errados                                             | ✅ `GetMostCommonMistakes` (parcial) |
| Erros e Acertos        | Erro recorrente no mesmo exercício                                     | ❌                                |
| Erros e Acertos        | Número de tentativas até acertar                                      | ❌                                |
| Gamificação            | Progresso rumo a badges/conquistas                                    | ❌                                |
| Gamificação            | Pontuação total acumulada                                              | ❌                                |
| Gamificação            | Exercício favorito (mais tentado com bom desempenho)                  | ❌                                |
| Gamificação            | “Desafio do dia” resolvido                                             | ❌                                |
| Gamificação            | Trilhas/missões completadas                                            | ❌                                |
| Recomendação           | Exercício que precisa ser revisado                                    | ✅ `GetRecommendations`           |
| Recomendação           | Recomendação de próximo exercício com base nos erros                  | ✅ `GetRecommendations` (parcial) |
| Recomendação           | Sugestão para subir a dificuldade                                      | ✅ `GetRecommendations` (parcial) |
| Recomendação           | Exercícios onde está acima da média                                   | ❌                                |
| Recomendação           | Melhor dia/sessão (pico de performance)                               | ❌                                |
| Histórico/Outros       | Linha do tempo de atividade                                            | ✅ `GetUserTimeline`              |
| Histórico/Outros       | Histórico das últimas tentativas                                      | ✅ `GetScoreHistory`              |