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
                Name = "HigherOrLower",
                Description = "Ouça duas notas e diga se a segunda é mais alta ou mais grave.",
                ExerciseTypeId = 1,
                ExerciseCategoryId = 4,
                DifficultyLevelId = 1,
                Instructions = "Ouça as duas notas tocadas em sequência e identifique se a segunda nota é mais alta ou mais grave que a primeira.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Concentre-se na direção do som.",
                    "Mais alta = o som 'sobe'.",
                    "Mais grave = o som 'desce'.",
                    "Cante as duas notas para sentir a diferença."
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
                            { "Mais alta", "higher" },
                            { "Mais grave", "lower" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessScaleType",
                Description = "Ouça uma escala e identifique o tipo: maior, menor ou pentatônica.",
                ExerciseTypeId = 8,
                ExerciseCategoryId = 5,
                DifficultyLevelId = 2,
                Instructions = "Ouça a escala tocada do início ao fim e identifique se é maior, menor, pentatônica maior ou pentatônica menor.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Maior soa alegre e brilhante.",
                    "Menor soa melancólica e escura.",
                    "Pentatônica tem menos notas — soa mais 'aberta'.",
                    "Preste atenção nos intervalos entre as notas."
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
                            { "Maior", "major" },
                            { "Menor", "minor" },
                            { "Pentatônica Maior", "majorPentatonic" },
                            { "Pentatônica Menor", "minorPentatonic" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "CompleteScale",
                Description = "Ouça a tônica e complete a escala selecionando as notas restantes na pauta.",
                ExerciseTypeId = 8,
                ExerciseCategoryId = 5,
                DifficultyLevelId = 2,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Key",
                        Name = "csRoot",
                        Options = new List<FilterOption>
                        {
                            new("any", "Exercise.Any"),
                            new("C", "C"),
                            new("C#", "C#"),
                            new("D", "D"),
                            new("Eb", "Eb"),
                            new("E", "E"),
                            new("F", "F"),
                            new("F#", "F#"),
                            new("G", "G"),
                            new("Ab", "Ab"),
                            new("A", "A"),
                            new("Bb", "Bb"),
                            new("B", "B"),
                            new("Db", "Db"),
                            new("Gb", "Gb")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Scale",
                        Name = "csScale",
                        Options = new List<FilterOption>
                        {
                            new("all", "Exercise.All"),
                            new("major", "Exercise.ScaleTypeMajor"),
                            new("minor", "Exercise.ScaleTypeMinor"),
                            new("majorPentatonic", "Exercise.ScaleTypeMajorPentatonic"),
                            new("minorPentatonic", "Exercise.ScaleTypeMinorPentatonic")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Octave",
                        Name = "csOctave",
                        Options = new List<FilterOption>
                        {
                            new("3", "3"),
                            new("4", "4")
                        }
                    }
                }),
                Instructions = "Você ouvirá a primeira nota (tônica) da escala. Use a paleta para preencher as notas seguintes em ordem ascendente.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Lembre dos intervalos: maior = T-T-S-T-T-T-S; menor natural = T-S-T-T-S-T-T.",
                    "Conte os semitons a partir da tônica para escolher cada nota.",
                    "Use ♯/♭ para ajustar uma nota recém-colocada.",
                    "Pentatônicas pulam alguns graus — só 5 notas além da tônica."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "CompleteChord",
                Description = "Ouça a fundamental e complete as notas restantes do acorde na pauta.",
                ExerciseTypeId = 2,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 2,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Quality.ChordGroup",
                        Name = "ccQuality",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.TypeChordMajeur"),
                            new("minor", "Exercise.TypeChordMineur"),
                            new("both", "Exercise.TypeChordMajeurMineur")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Octave",
                        Name = "ccOctave",
                        Options = new List<FilterOption>
                        {
                            new("3", "3"),
                            new("4", "4"),
                            new("5", "5")
                        }
                    }
                }),
                Instructions = "Você ouvirá a fundamental do acorde. Use a paleta para completar a terça e a quinta na pauta.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Acordes maiores usam terça maior e quinta justa.",
                    "Acordes menores usam terça menor e quinta justa.",
                    "Conte semitons a partir da fundamental se precisar confirmar o intervalo."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "TransposeScale",
                Description = "Ouça uma escala e escreva sua transposição para a tonalidade-alvo.",
                ExerciseTypeId = 8,
                ExerciseCategoryId = 5,
                DifficultyLevelId = 3,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Key",
                        Name = "tsRoot",
                        Options = new List<FilterOption>
                        {
                            new("any", "Exercise.Any"),
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
                        Name = "tsScale",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.Major"),
                            new("minor", "Exercise.Minor")
                        }
                    }
                }),
                Instructions = "Ouça a escala original. O sistema mostrará a tonalidade-alvo; escreva a escala transposta na pauta.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Preserve o padrão de intervalos da escala original.",
                    "Comece pela nova tônica e suba grau por grau.",
                    "Use acidentes para manter a distância correta entre os graus."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "MelodicDictation",
                Description = "Ouça uma melodia curta e escreva as notas e ritmos na pauta.",
                ExerciseTypeId = 5,
                ExerciseCategoryId = 2,
                DifficultyLevelId = 3,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "mdLevel",
                        Options = new List<FilterOption>
                        {
                            new("1", "Beginner"),
                            new("3", "Intermediate"),
                            new("5", "Advanced")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.MelodyLength",
                        Name = "mdMeasures",
                        Options = new List<FilterOption>
                        {
                            new("short", "Exercise.Short"),
                            new("long", "Exercise.Long")
                        }
                    }
                }),
                Instructions = "Escute a melodia completa e reproduza as notas, durações, pausas e barras de compasso na pauta.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Ouça primeiro o contorno geral antes de escrever.",
                    "Marque as durações principais e depois ajuste as alturas.",
                    "Use barras de compasso para separar as frases quando necessário."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "RhythmDictation",
                Description = "Ouça um ritmo em uma nota fixa e escreva a sequência rítmica.",
                ExerciseTypeId = 6,
                ExerciseCategoryId = 3,
                DifficultyLevelId = 2,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "rdLevel",
                        Options = new List<FilterOption>
                        {
                            new("1", "Beginner"),
                            new("3", "Intermediate"),
                            new("5", "Advanced")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.MelodyLength",
                        Name = "rdMeasures",
                        Options = new List<FilterOption>
                        {
                            new("short", "Exercise.Short"),
                            new("long", "Exercise.Long")
                        }
                    }
                }),
                Instructions = "Escute o padrão rítmico tocado em uma nota fixa e escreva as durações, pausas e barras de compasso.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Conte os pulsos em voz baixa enquanto escuta.",
                    "Identifique primeiro os valores longos e depois preencha os curtos.",
                    "No ditado rítmico, a altura não importa; o sistema corrige apenas o ritmo."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "GuessInversion",
                Description = "Ouça um acorde tríade e identifique se está no estado fundamental, na 1ª ou na 2ª inversão.",
                ExerciseTypeId = 2,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 3,
                Instructions = "Ouça o acorde e identifique sua posição: Fundamental (raiz no baixo), 1ª Inversão (terça no baixo) ou 2ª Inversão (quinta no baixo).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Fundamental: a nota mais grave é a tônica do acorde.",
                    "1ª Inversão: a nota mais grave é a terça — soa mais 'flutuante'.",
                    "2ª Inversão: a nota mais grave é a quinta — soa instável, pede resolução.",
                    "Foque no intervalo entre o baixo e a próxima nota."
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
                            { "Fundamental", "root" },
                            { "1ª Inversão", "first" },
                            { "2ª Inversão", "second" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessCadence",
                Description = "Ouça uma progressão de 4 acordes e identifique a cadência: perfeita, plagal, imperfeita ou deceptiva.",
                ExerciseTypeId = 7,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 3,
                Instructions = "Ouça os 4 acordes em sequência. Os dois primeiros estabelecem o contexto tonal; os dois últimos definem o tipo de cadência.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Cadência Perfeita (V → I) soa conclusiva, como um ponto final.",
                    "Cadência Plagal (IV → I) é a 'cadência amém' — gentil e estável.",
                    "Cadência Imperfeita (… → V) termina suspensa, sem resolver.",
                    "Cadência Deceptiva (V → vi) surpreende: parece resolver mas vai para outro lugar."
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
                            { "Perfeita", "perfect" },
                            { "Plagal", "plagal" },
                            { "Imperfeita", "imperfect" },
                            { "Deceptiva", "deceptive" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessGreekMode",
                Description = "Ouça uma escala modal e identifique qual dos sete modos gregos está sendo tocado.",
                ExerciseTypeId = 8,
                ExerciseCategoryId = 5,
                DifficultyLevelId = 3,
                Instructions = "Ouça a escala completa e identifique qual modo grego está sendo tocado: Jônio, Dórico, Frígio, Lídio, Mixolídio, Eólio ou Lócrio.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Jônio é a escala maior tradicional — soa alegre e estável.",
                    "Dórico tem um caráter menor mas com a sexta maior — soa elegante.",
                    "Frígio tem a segunda menor — soa exótico, com cor espanhola.",
                    "Lídio tem a quarta aumentada — soa etéreo, sonhador.",
                    "Mixolídio é maior com a sétima menor — soa bluesy/celta.",
                    "Eólio é a escala menor natural — soa melancólico.",
                    "Lócrio tem a quinta diminuta — soa instável, dissonante."
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
                            { "Jônio", "ionian" },
                            { "Dórico", "dorian" },
                            { "Frígio", "phrygian" },
                            { "Lídio", "lydian" },
                            { "Mixolídio", "mixolydian" },
                            { "Eólio", "aeolian" },
                            { "Lócrio", "locrian" }
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
                Name = "IntervalMelodico",
                Description = "Identifique graus e intervalos de uma melodia",
                ExerciseTypeId = 3, // IntervalRecognition
                ExerciseCategoryId = 2, // Melody
                DifficultyLevelId = 2, // Intermediate
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
                        Label = "Exercise.Scale",
                        Name = "scaleTypeSelect",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.ScaleMajor"),
                            new("minor", "Exercise.ScaleMinor"),
                            new ("both", "Exercise.ScaleBoth")
                        }
                    }
                }),
                Instructions = "Escute a melodia e identifique: o primeiro grau, o último grau, o intervalo entre as duas primeiras notas e o intervalo entre as duas últimas notas.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Identifique primeiro a tonalidade da melodia.",
                    "Preste atenção à primeira e última nota para determinar os graus.",
                    "Compare os intervalos com intervalos conhecidos.",
                    "Use as escalas como referência para os graus."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    // No answer buttons needed - uses custom dropdowns in the view
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