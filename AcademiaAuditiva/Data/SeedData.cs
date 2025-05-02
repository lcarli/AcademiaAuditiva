using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using Newtonsoft.Json;

public static class SeedData
{
    public static void SeedExercises(ApplicationDbContext context)
    {
        // Seed para ExerciseType
        if (!context.ExerciseTypes.Any())
        {
            context.ExerciseTypes.AddRange(
                new ExerciseType { Name = "NoteRecognition", DisplayName = "Reconhecimento de Notas" },
                new ExerciseType { Name = "ChordRecognition", DisplayName = "Reconhecimento de Acordes" },
                new ExerciseType { Name = "IntervalRecognition", DisplayName = "Reconhecimento de Intervalos" },
                new ExerciseType { Name = "FunctionRecognition", DisplayName = "Reconhecimento de Funções Harmônicas" },
                new ExerciseType { Name = "MelodyReproduction", DisplayName = "Reprodução de Melodias" },
                new ExerciseType { Name = "RhythmPatterns", DisplayName = "Padrões Rítmos" },
                new ExerciseType { Name = "HarmonicField", DisplayName = "Campo Harmônico" },
                new ExerciseType { Name = "ScaleRecognition", DisplayName = "Reconhecimento de Escalas" }
            );
        }

        // Seed para ExerciseCategory
        if (!context.ExerciseCategories.Any())
        {
            context.ExerciseCategories.AddRange(
                new ExerciseCategory { Name = "Harmony", DisplayName = "Harmonia" },
                new ExerciseCategory { Name = "Melody", DisplayName = "Melodia" },
                new ExerciseCategory { Name = "Rhythm", DisplayName = "Ritmo" },
                new ExerciseCategory { Name = "EarTraining", DisplayName = "Treinamento Auditivo" },
                new ExerciseCategory { Name = "Scales", DisplayName = "Escalas" },
                new ExerciseCategory { Name = "Games", DisplayName = "Jogos" },
                new ExerciseCategory { Name = "Misc", DisplayName = "Diversos" }
            );
        }

        // Seed para DifficultyLevel
        if (!context.DifficultyLevels.Any())
        {
            context.DifficultyLevels.AddRange(
                new DifficultyLevel { Name = "Beginner", DisplayName = "Iniciante" },
                new DifficultyLevel { Name = "Intermediate", DisplayName = "Intermediário" },
                new DifficultyLevel { Name = "Advanced", DisplayName = "Avançado" }
            );
        }

        // Seed para Badges
        if (!context.Badges.Any())
        {
            context.Badges.AddRange(
                // Categoria
                new Badge { BadgeKey = "master_chords", Title = "Mestre dos Acordes", Description = "90% em 3 exercícios de acordes" },
                new Badge { BadgeKey = "sharp_listener", Title = "Ouvinte Afiado", Description = "90% em 3 de percepção" },
                new Badge { BadgeKey = "rhythm_maestro", Title = "Maestro do Ritmo", Description = "100% em 2 de ritmo" },
                new Badge { BadgeKey = "melody_explorer", Title = "Explorador Melódico", Description = "80% em todos os exercícios de melodia" },
                new Badge { BadgeKey = "scale_climber", Title = "Escalador de Tons", Description = "Usou todos os tipos de escalas" },

                // Esforço
                new Badge { BadgeKey = "3_days", Title = "3 Dias Seguidos", Description = "Praticou 3 dias consecutivos" },
                new Badge { BadgeKey = "5_days", Title = "5 Dias Seguidos", Description = "Praticou 5 dias consecutivos" },
                new Badge { BadgeKey = "marathon_20min", Title = "Maratona 20min", Description = "20 minutos sem parar" },
                new Badge { BadgeKey = "faithful_practitioner", Title = "Praticante Fiel", Description = "Completou 30 sessões" },
                new Badge { BadgeKey = "explorer", Title = "Explorador", Description = "Usou todos os filtros uma vez" },
                new Badge { BadgeKey = "filter_ninja", Title = "Filtro Ninja", Description = "Usou combinações personalizadas em 5 sessões" },
                new Badge { BadgeKey = "first_session", Title = "Iniciador de Jornada", Description = "Primeira sessão realizada" },
                new Badge { BadgeKey = "10_sessions_week", Title = "10 Sessões em 1 Semana", Description = "Alta frequência semanal" },
                new Badge { BadgeKey = "daily_challenge_complete", Title = "Desafio Diário Completo", Description = "Completou todos os exercícios do dia" },

                // Evolução
                new Badge { BadgeKey = "comeback_kid", Title = "Deu a Volta por Cima", Description = "Começou errando e depois passou de 80%" },
                new Badge { BadgeKey = "advanced_conqueror", Title = "Conquistador Avançado", Description = "5 exercícios de nível avançado" },
                new Badge { BadgeKey = "persistent_student", Title = "Aluno Persistente", Description = "Melhorou pontuação em 3 tentativas seguidas" },
                new Badge { BadgeKey = "total_mastery", Title = "Domínio Total", Description = "100% em um exercício com filtros completos" },
                new Badge { BadgeKey = "notable_progress", Title = "Evolução Notável", Description = "Melhorou em todas as categorias em 1 mês" },
                new Badge { BadgeKey = "resilient_ear", Title = "Resiliência Auditiva", Description = "Acertou após 3 erros seguidos" },
                new Badge { BadgeKey = "interval_tamer", Title = "Domador de Intervalos", Description = "10 sessões de intervalos com +80%" },

                // Diversão
                new Badge { BadgeKey = "mission_addict", Title = "Viciado em Missões", Description = "Completou 10 desafios mistos" },
                new Badge { BadgeKey = "speedster", Title = "Speedster", Description = "90% de acerto em um SpeedTest" },
                new Badge { BadgeKey = "mystery_listener", Title = "Ouvinte Misterioso", Description = "Acertou uma questão impossível (modo aleatório total)" },
                new Badge { BadgeKey = "impossible_melody", Title = "Melodia Impossível", Description = "Acertou uma melodia alterada com pausa escondida" },
                new Badge { BadgeKey = "badge_collector", Title = "Colecionador de Badges", Description = "Obteve 15 conquistas" }
            );
        }

        var exercises = new List<Exercise>
        {
            new Exercise {
                Name = "GuessNote",
                Description = "Adivinhe a Nota tocada",
                ExerciseTypeId = 1,
                ExerciseCategoryId = 4,
                DifficultyLevelId = 1,
                Instructions = "Ouça a nota tocada e selecione a nota correspondente entre as opções disponíveis.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Ouça mais de uma vez se necessário.",
                    "Tente cantar a nota para comparar com seu registro mental.",
                    "Compare com notas que você conhece bem como Dó ou Lá."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "C", "C" },
                            { "C#", "C#" },
                            { "D", "D" },
                            { "D#", "D#" },
                            { "E", "E" },
                            { "F", "F" },
                            { "F#", "F#" },
                            { "G", "G" },
                            { "G#", "G#" },
                            { "A", "A" },
                            { "A#", "A#" },
                            { "B", "B" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessChords",
                Description = "Reconhecimento de acordes",
                ExerciseTypeId = 2,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 1,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.TypeChord",
                        Name = "chordType",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.TypeChordMajeur"),
                            new("minor", "Exercise.TypeChordMineur"),
                            new("both", "Exercise.TypeChordMajeurMineur"),
                            new("all", "Exercise.TypeChordAll")
                        }
                    }
                }),
                Instructions = "Ouça o acorde tocado e selecione o tipo de acorde correspondente.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Preste atenção na sensação sonora: maior tende a soar feliz, menor mais triste.",
                    "Compare com acordes de referência se necessário.",
                    "Tente cantar as notas para perceber se há intervalos maiores ou menores."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "C", "C" },
                            { "C#", "C#" },
                            { "D", "D" },
                            { "D#", "D#" },
                            { "E", "E" },
                            { "F", "F" },
                            { "F#", "F#" },
                            { "G", "G" },
                            { "G#", "G#" },
                            { "A", "A" },
                            { "A#", "A#" },
                            { "B", "B" }
                        }
                    },
                    { "guessQuality", new Dictionary<string, string>
                        {
                            { "Major", "major" },
                            { "Minor", "minor" },
                            { "Diminished", "diminished" },
                            { "Augmented", "augmented" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessInterval",
                Description = "Adivinhe o Intervalo tocado",
                ExerciseTypeId = 3,
                ExerciseCategoryId = 4,
                DifficultyLevelId = 1,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Key",
                        Name = "keySelect",
                        Options = new List<FilterOption>
                        {
                            new("C4", "C"),
                            new("C#4", "C#"),
                            new("D4", "D"),
                            new("D#4", "D#"),
                            new("E4", "E"),
                            new("F4", "F"),
                            new("F#4", "F#"),
                            new("G4", "G"),
                            new("G#4", "G#"),
                            new("A4", "A"),
                            new("A#4", "A#"),
                            new("B4", "B")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Scale",
                        Name = "scaleTypeSelect",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.Major"),
                            new("minor", "Exercise.Minor")
                        }
                    }
                }),
                Instructions = "Ouça as duas notas e identifique a distância entre elas (intervalo).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Associe intervalos a músicas conhecidas (ex: terça maior = 'Parabéns pra você').",
                    "Ouça repetidamente e cante as notas.",
                    "Perceba se o som é próximo (segunda) ou espaçado (quinta, oitava...)."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay",
                    "Note1",
                    "Note2"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "2", "2" },
                            { "3", "3" },
                            { "4", "4" },
                            { "5", "5" },
                            { "6", "6" },
                            { "7", "7" },
                            { "8", "8" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessQuality",
                Description = "Adivinhe a qualidade do acorde tocado.",
                ExerciseTypeId = 2,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 2,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Quality.ChordGroup",
                        Name = "chordGroup",
                        Options = new List<FilterOption>
                        {
                            new("both", "Exercise.TypeChordMajeurMineur"),
                            new("all", "Exercise.All")
                        }
                    }
                }),
                Instructions = "Ouça o acorde e determine se ele é maior, menor ou diminuto.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Tente memorizar a sonoridade típica de cada qualidade.",
                    "Acordes diminutos soam mais tensos ou instáveis.",
                    "Compare com acordes simples que você já conhece."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "M", "major" },
                            { "M7", "major7" },
                            { "m", "minor" },
                            { "m7", "minor7" },
                            { "dim", "diminished" },
                            { "dim7", "diminished7" },
                            { "aug", "augmented" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessFunction",
                Description = "Adivinhe a função do acorde dentro do campo harmônico.",
                ExerciseTypeId = 4,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 2,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Key",
                        Name = "keySelect",
                        Options = new List<FilterOption>
                        {
                            new("C", "C"),
                            new("C#", "C#"),
                            new("D", "D"),
                            new("D#", "D#"),
                            new("E", "E"),
                            new("F", "F"),
                            new("F#", "F#"),
                            new("G", "G"),
                            new("G#", "G#"),
                            new("A", "A"),
                            new("A#", "A#"),
                            new("B", "B")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Scale",
                        Name = "scaleTypeSelect",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.Major"),
                            new("minor", "Exercise.Minor")
                        }
                    }
                }),
                Instructions = "Ouça o acorde dentro de um contexto e identifique sua função (tônica, dominante, subdominante).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Lembre que acordes tônicos tendem a soar resolvidos.",
                    "Dominantes soam como tensão que pede resolução.",
                    "Estude o campo harmônico em diferentes tonalidades."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "I", "1-major" },
                            { "ii", "2-minor" },
                            { "iii", "3-minor" },
                            { "IV", "4-major" },
                            { "V", "5-major" },
                            { "vi", "6-minor" },
                            { "VII°", "7-diminished" },
                            { "i", "1-minor" },
                            { "II°", "2-diminished" },
                            { "III", "3-major" },
                            { "iv", "4-minor" },
                            { "v", "5-minor" },
                            { "VI", "6-major" },
                            { "VII", "7-major" }
                        }
                    }
                })
            },
           new Exercise {
                Name = "GuessFullInterval",
                Description = "Adivinhe o intervalo completo (maior, menor, justo...)",
                ExerciseTypeId = 3,
                ExerciseCategoryId = 4,
                DifficultyLevelId = 3,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Key",
                        Name = "keySelect",
                        Options = new List<FilterOption>
                        {
                            new("C", "C"),
                            new("D", "D"),
                            new("E", "E"),
                            new("F", "F"),
                            new("G", "G"),
                            new("A", "A"),
                            new("B", "B")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Direction",
                        Name = "intervalDirection",
                        Options = new List<FilterOption>
                        {
                            new("asc", "Exercise.Direction.Asc"),
                            new("desc", "Exercise.Direction.Desc"),
                            new("both", "Exercise.Direction.Both")
                        }
                    }
                }),
                Instructions = "Ouça o intervalo e identifique não apenas a distância, mas também a sua qualidade (maior, menor, justo, etc).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Treine com intervalos simples antes de ir para os compostos.",
                    "Associe sons familiares a cada tipo de intervalo.",
                    "Intervalos justos (como quarta e quinta) têm sonoridade estável."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay",
                    "Note1",
                    "Note2"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "2m", "2m" },
                            { "2M", "2M" },
                            { "3m", "3m" },
                            { "3M", "3M" },
                            { "4J", "4J" },
                            { "5d", "5d" },
                            { "5J", "5J" },
                            { "6m", "6m" },
                            { "6M", "6M" },
                            { "7m", "7m" },
                            { "7M", "7M" },
                            { "8J", "8J" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessMissingNote",
                Description = "Ouça duas melodias e diga se são iguais ou diferentes.",
                ExerciseTypeId = 5,
                ExerciseCategoryId = 2,
                DifficultyLevelId = 1,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.MelodyLength",
                        Name = "melodyLength",
                        Options = new List<FilterOption>
                        {
                            new("4", "4"),
                            new("5", "5"),
                            new("6", "6"),
                            new("7", "7"),
                            new("8", "8")
                        }
                    }
                }),
                Instructions = "Ouça duas melodias e diga se são iguais ou se houve alguma alteração entre elas.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Preste atenção nas notas centrais da melodia.",
                    "Se não tiver certeza, ouça mais de uma vez.",
                    "Cantar ou batucar a melodia pode ajudar na memorização."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay",
                    "Melody1",
                    "Melody2"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "Iguais", "same" },
                            { "Diferentes", "diff" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "SolfegeMelody",
                Description = "Leia a melodia e cante-a.",
                ExerciseTypeId = 5,
                ExerciseCategoryId = 2,
                DifficultyLevelId = 1,
                Instructions = "Leia a melodia exibida e tente cantá-la corretamente.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Observe atentamente as alturas das notas na partitura.",
                    "Cante devagar para garantir precisão.",
                    "Se necessário, pratique com escalas antes de tentar."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Generate",
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "Correto", "correct" },
                            { "Errado", "incorrect" }
                        }
                    }
                })
            }
        };

        foreach (var ex in exercises)
        {
            var existing = context.Exercises.FirstOrDefault(e => e.Name == ex.Name);
            if (existing == null)
            {
                context.Exercises.Add(ex);
            }
            else
            {
                existing.Description = ex.Description;
                existing.ExerciseTypeId = ex.ExerciseTypeId;
                existing.ExerciseCategoryId = ex.ExerciseCategoryId;
                existing.DifficultyLevelId = ex.DifficultyLevelId;
                existing.FiltersJson = ex.FiltersJson;
                existing.Instructions = ex.Instructions;
                existing.TipsJson = ex.TipsJson;
                existing.AudioButtonsJson = ex.AudioButtonsJson;
                existing.AnswerButtonsJson = ex.AnswerButtonsJson;
                context.Exercises.Update(existing);
            }
        }

        context.SaveChanges();
    }
}