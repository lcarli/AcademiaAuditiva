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

        // Badges: add the keys that are missing; existing rows keep their texts.
        // Players see the localized texts from the resource files (BadgeCatalog).
        var seededBadges = context.Badges.Select(b => b.BadgeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        context.Badges.AddRange(BadgeSeed().Where(b => !seededBadges.Contains(b.BadgeKey)));

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
                Description = "Ouça um acorde e escreva-o na pauta.",
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
                            new("both", "Exercise.TypeChordMajeurMineur"),
                            new("triads", "Exercise.CompleteChord.Triads"),
                            new("sevenths", "Exercise.CompleteChord.Sevenths"),
                            new("all", "Exercise.CompleteChord.All")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.CompleteChord.AccidentalsFilter",
                        Name = "ccAccidentals",
                        Options = new List<FilterOption>
                        {
                            new("none", "Exercise.CompleteChord.NoAccidentals"),
                            new("any", "Exercise.CompleteChord.WithAccidentals")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.CompleteChord.RootFilter",
                        Name = "ccRoot",
                        Options = new List<FilterOption>
                        {
                            new("given", "Exercise.CompleteChord.RootGiven"),
                            new("hidden", "Exercise.CompleteChord.RootHidden")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Octave",
                        Name = "ccOctave",
                        Options = new List<FilterOption>
                        {
                            new("4", "Exercise.CompleteChord.Octave4"),
                            new("3", "Exercise.CompleteChord.Octave3")
                        }
                    }
                }),
                Instructions = "Clique em Tocar para ouvir o acorde. Escreva as notas dele empilhadas na pauta, da fundamental para cima. Se a fundamental já estiver na pauta (em cinza), escreva só as outras.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Acordes maiores têm terça maior e quinta justa; menores, terça menor e quinta justa.",
                    "O acorde diminuto tem a quinta diminuta, meio tom abaixo da justa; o aumentado, a quinta aumentada, meio tom acima.",
                    "Nos acordes com sétima, ouça a sétima: maior (meio tom abaixo da oitava), menor (um tom abaixo) ou diminuta (um tom e meio abaixo)."
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
                            new("4", "Advanced")
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
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Tempo",
                        Name = "mdTempo",
                        Options = new List<FilterOption>
                        {
                            new("120", "Exercise.Tempo.Normal"),
                            new("90", "Exercise.Tempo.Slow"),
                            new("60", "Exercise.Tempo.VerySlow")
                        }
                    }
                }),
                Instructions = "Uma contagem dá o andamento e depois a melodia toca. Escolha uma figura e clique nos nomes das notas em ordem; uma pausa entra com um clique. As barras de compasso entram sozinhas.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Ouça primeiro o contorno geral antes de escrever.",
                    "Marque as durações principais e depois ajuste as alturas.",
                    "Clique em uma nota na pauta para mudar a oitava ou o acidente."
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
                            new("4", "Advanced"),
                            new("5", "Exercise.RhythmLevel.Dotted"),
                            new("6", "Exercise.RhythmLevel.Sixteenths"),
                            new("7", "Exercise.RhythmLevel.Syncopation"),
                            new("8", "Exercise.RhythmLevel.Compound")
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
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Tempo",
                        Name = "rdTempo",
                        Options = new List<FilterOption>
                        {
                            new("120", "Exercise.Tempo.Normal"),
                            new("90", "Exercise.Tempo.Slow"),
                            new("60", "Exercise.Tempo.VerySlow")
                        }
                    }
                }),
                Instructions = "Uma contagem dá o andamento e depois o ritmo toca. Clique nas figuras e pausas na ordem em que soam. As barras de compasso entram sozinhas.",
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
                Name = "GuessMeter",
                Description = "Ouça doze pulsos e identifique o compasso: binário, ternário ou quaternário.",
                ExerciseTypeId = 6,
                ExerciseCategoryId = 3,
                DifficultyLevelId = 1,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "gmLevel",
                        Options = new List<FilterOption>
                        {
                            new("clicks", "Exercise.MeterLevel.Clicks"),
                            new("accompaniment", "Exercise.MeterLevel.Accompaniment")
                        }
                    }
                }),
                Instructions = "Ouça doze pulsos e identifique o compasso. O primeiro tempo de cada compasso é acentuado: os pulsos se agrupam de dois em dois (binário), de três em três (ternário) ou de quatro em quatro (quaternário).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Conte \"1, 2, 3…\" e volte ao 1 em cada acento: o maior número a que você chega é o compasso.",
                    "Binário e quaternário se confundem: no quaternário, três tempos fracos separam os acentos.",
                    "Com acompanhamento, o baixo e a troca de acorde marcam o primeiro tempo, e os acordes curtos, os outros."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                // The meters MusicTheoryService draws (Meters); the page labels them by value.
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "Binário", "duple" },
                            { "Ternário", "triple" },
                            { "Quaternário", "quadruple" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessRhythmPattern",
                Description = "Ouça um ritmo e identifique-o entre quatro ritmos escritos.",
                ExerciseTypeId = 6,
                ExerciseCategoryId = 3,
                DifficultyLevelId = 1,
                // The levels of RhythmDictation (DictationRhythm), which draws its rhythms.
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "grpLevel",
                        Options = new List<FilterOption>
                        {
                            new("1", "Beginner"),
                            new("3", "Intermediate"),
                            new("4", "Advanced"),
                            new("5", "Exercise.RhythmLevel.Dotted"),
                            new("6", "Exercise.RhythmLevel.Sixteenths"),
                            new("7", "Exercise.RhythmLevel.Syncopation"),
                            new("8", "Exercise.RhythmLevel.Compound")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Tempo",
                        Name = "grpTempo",
                        Options = new List<FilterOption>
                        {
                            new("120", "Exercise.Tempo.Normal"),
                            new("90", "Exercise.Tempo.Slow"),
                            new("60", "Exercise.Tempo.VerySlow")
                        }
                    }
                }),
                Instructions = "Uma contagem dá o andamento e depois um ritmo de dois compassos toca. Escolha, entre os quatro ritmos escritos, o que você ouviu.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Os quatro ritmos diferem em um ou dois tempos: compare-os antes de ouvir e procure onde mudam.",
                    "Acompanhe a partitura enquanto escuta, contando os tempos de cada compasso.",
                    "Os ritmos sempre diferem em onde as notas começam: concentre-se nos ataques, mais do que na duração das notas."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "RhythmTap",
                Description = "Ouça um ritmo e repita-o, batendo-o no botão ou na barra de espaço.",
                ExerciseTypeId = 6,
                ExerciseCategoryId = 3,
                DifficultyLevelId = 1,
                // The levels of RhythmDictation (DictationRhythm), which draws its rhythms.
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "rtLevel",
                        Options = new List<FilterOption>
                        {
                            new("1", "Beginner"),
                            new("3", "Intermediate"),
                            new("4", "Advanced"),
                            new("5", "Exercise.RhythmLevel.Dotted"),
                            new("6", "Exercise.RhythmLevel.Sixteenths"),
                            new("7", "Exercise.RhythmLevel.Syncopation"),
                            new("8", "Exercise.RhythmLevel.Compound")
                        }
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Tempo",
                        Name = "rtTempo",
                        Options = new List<FilterOption>
                        {
                            new("120", "Exercise.Tempo.Normal"),
                            new("90", "Exercise.Tempo.Slow"),
                            new("60", "Exercise.Tempo.VerySlow")
                        }
                    }
                }),
                Instructions = "Uma contagem dá o andamento, um ritmo de dois compassos toca e a contagem se repete. No tempo seguinte, bata o ritmo no botão ou na barra de espaço: uma batida onde começa cada nota.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Conte os tempos enquanto ouve e continue contando durante a segunda contagem: ela dá o andamento das suas batidas.",
                    "Só os ataques contam: nas notas longas e nas pausas, espere contando, sem bater.",
                    "Você pode começar quando quiser depois da contagem e bater um pouco mais rápido ou mais devagar: o que conta é a distância entre as batidas."
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
                Name = "GuessTopNote",
                Description = "Ouça um acorde de quatro vozes, com a fundamental no baixo, e identifique a nota de cima: a fundamental, a terça ou a quinta.",
                ExerciseTypeId = 2,
                ExerciseCategoryId = 1,
                DifficultyLevelId = 3,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.TypeChord",
                        Name = "tnQuality",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.TypeChordMajeur"),
                            new("minor", "Exercise.TypeChordMineur"),
                            new("both", "Exercise.TypeChordMajeurMineur")
                        }
                    }
                }),
                Instructions = "Ouça o acorde e identifique qual das notas dele está em cima, na voz mais aguda: a fundamental, a terça ou a quinta. O baixo é sempre a fundamental.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Cante a nota mais aguda do acorde, como se fosse a melodia, e depois desça até o baixo.",
                    "Se a nota de cima é a mesma do baixo, em outra oitava, ela é a fundamental: o acorde soa conclusivo.",
                    "A terça em cima soa doce e cheia; a quinta em cima soa aberta, como se o acorde ficasse suspenso."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                // The tones MusicTheoryService asks on top (TopNoteVoicings); the page labels them by value.
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "Fundamental", "topRoot" },
                            { "Terça", "topThird" },
                            { "Quinta", "topFifth" }
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
                Name = "GuessProgression",
                Description = "Ouça a cadência da tonalidade e depois uma progressão de acordes, e identifique-a.",
                ExerciseTypeId = 7,
                ExerciseCategoryId = 1,
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
                            new("B", "B"),
                            new("any", "Exercise.Any")
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
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "gpLevel",
                        Options = new List<FilterOption>
                        {
                            new("name", "Exercise.GuessProgression.LevelName"),
                            new("numerals", "Exercise.GuessProgression.LevelNumerals")
                        }
                    }
                }),
                Instructions = "Ouça a cadência que estabelece a tonalidade e depois uma progressão de acordes, e diga qual é a progressão. No nível de ditado, a progressão começa na tônica: identifique o grau de cada um dos três acordes seguintes.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Siga o baixo: ele anda para um grau vizinho ou salta uma quarta ou uma quinta.",
                    "O V puxa para o I: depois dele, o I soa como chegada e o vi como surpresa.",
                    "Em menor, o VII fica um tom abaixo da tônica, e o V tem a sensível, meio tom abaixo dela."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                // The progressions of either kind of key (MusicTheoryService's progression tables): the
                // page shows those of the key picked. Dictation answers on the page's selects instead.
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "I–V–vi–IV", "I-V-vi-IV" },
                            { "I–vi–IV–V", "I-vi-IV-V" },
                            { "ii–V–I", "ii-V-I" },
                            { "Blues de 12 compassos", "blues" },
                            { "i–VII–VI–V", "i-VII-VI-V" },
                            { "i–VI–III–VII", "i-VI-III-VII" },
                            { "ii°–V–i", "iio-V-i" }
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
                    },
                    IntervalModeFilter()
                }),
                Instructions = "Ouça as duas notas e identifique a distância entre elas (intervalo).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Associe intervalos a músicas conhecidas (ex: terça maior = 'Parabéns pra você').",
                    "Ouça repetidamente e cante as notas.",
                    "Perceba se o som é próximo (segunda) ou espaçado (quinta, oitava...).",
                    "Num intervalo harmônico, cante a nota de baixo e depois a de cima: ele vira um intervalo melódico."
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
                            new("triads", "Exercise.CompleteChord.Triads"),
                            new("sevenths", "Exercise.CompleteChord.Sevenths"),
                            new("susAdded", "Exercise.Quality.SusAdded"),
                            new("triadsSevenths", "Exercise.Quality.TriadsSevenths"),
                            new("all", "Exercise.CompleteChord.All")
                        }
                    }
                }),
                Instructions = "Ouça o acorde e diga a sua qualidade: maior, menor, diminuto, aumentado, um acorde com sétima, ou um sus, 6 ou add9. O filtro Tipo de acorde escolhe quais tocam.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Tente memorizar a sonoridade típica de cada qualidade.",
                    "Acordes diminutos soam mais tensos ou instáveis.",
                    "Compare com acordes simples que você já conhece.",
                    "O dominante 7 pede resolução; o maior 7 repousa, suave e brilhante.",
                    "Os sus não têm terça: nem maiores nem menores, soam abertos, à espera."
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
                            { "m", "minor" },
                            { "dim", "diminished" },
                            { "aug", "augmented" },
                            { "M7", "major7" },
                            { "7", "dominant7" },
                            { "m7", "minor7" },
                            { "m7(♭5)", "halfDiminished" },
                            { "dim7", "diminished7" },
                            { "sus2", "sus2" },
                            { "sus4", "sus4" },
                            { "6", "major6" },
                            { "add9", "add9" }
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
                Instructions = "Ouça a cadência que estabelece a tonalidade e depois o acorde, e identifique a função dele nessa tonalidade (tônica, subdominante, dominante...).",
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
                            { "VI", "6-major" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessDegree",
                Description = "Ouça a cadência da tonalidade e identifique o grau de uma nota.",
                ExerciseTypeId = 1,
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
                            new("B", "B"),
                            new("any", "Exercise.Any")
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
                    },
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "gdLevel",
                        Options = new List<FilterOption>
                        {
                            new("diatonic", "Exercise.GuessDegree.Diatonic"),
                            new("chromatic", "Exercise.GuessDegree.Chromatic")
                        }
                    }
                }),
                Instructions = "Ouça a cadência que estabelece a tonalidade e depois uma nota, e identifique o grau dela nessa tonalidade (1 é a tônica).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Cante a escala a partir da tônica até chegar à nota.",
                    "A sensível (o 7 do modo maior) puxa para a tônica, e o 4 tende a descer para o 3.",
                    "O 1, o 3 e o 5 soam estáveis: são as notas do acorde da tônica."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                // Every degree either kind of key asks, in the order of their semitones. The page shows
                // 1 to 7, and on the chromatic level ♭2, ♭3, ♯4, ♭6 and ♭7 in a major key or ♭2, ♯3,
                // ♯4, ♯6 and ♯7 in a minor one (MusicTheoryService's degree tables).
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessAnswer", new Dictionary<string, string>
                        {
                            { "1", "1" },
                            { "♭2", "b2" },
                            { "2", "2" },
                            { "♭3", "b3" },
                            { "3", "3" },
                            { "♯3", "#3" },
                            { "4", "4" },
                            { "♯4", "#4" },
                            { "5", "5" },
                            { "♭6", "b6" },
                            { "6", "6" },
                            { "♯6", "#6" },
                            { "♭7", "b7" },
                            { "7", "7" },
                            { "♯7", "#7" }
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
                    IntervalModeFilter(),
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
                    "Intervalos justos (como quarta e quinta) têm sonoridade estável.",
                    "Tocadas juntas, segundas e sétimas soam ásperas, terças e sextas soam doces, e quartas, quintas e oitavas soam ocas."
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
                Name = "GuessChangedNote",
                Description = "Ouça duas melodias e diga qual nota mudou e se ela subiu ou desceu.",
                ExerciseTypeId = 5,
                ExerciseCategoryId = 2,
                DifficultyLevelId = 2,
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
                Instructions = "Ouça uma melodia e depois a mesma melodia com uma nota trocada. Diga qual nota mudou, contando a partir da primeira, e se ela subiu ou desceu.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Conte as notas enquanto ouve: só uma delas muda.",
                    "Ouça cada melodia sozinha com os botões Melodia 1 e Melodia 2.",
                    "A nota que mudou andou um grau da escala ou pulou uma terça; as outras continuam iguais.",
                    "As melodias terminam na tônica: se a última nota mudou, a segunda melodia não soa conclusiva."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay",
                    "Melody1",
                    "Melody2"
                }),
                // The note's place in the melody, from 1 (GuessChangedNote.js hides those past
                // the round's length), and where it went.
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>
                {
                    { "guessPosition", Enumerable.Range(1, 8).ToDictionary(n => n.ToString(), n => n.ToString()) },
                    { "guessDirection", new Dictionary<string, string>
                        {
                            { "Subiu", "up" },
                            { "Desceu", "down" }
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
            },
            // The singing exercises: the student records themselves and the browser sends the
            // notes it heard (SingNote.js, SingInterval.js and SingMelody.js), so they have no
            // answer buttons.
            new Exercise {
                Name = "SingNote",
                Description = "Ouça uma nota e cante-a.",
                ExerciseTypeId = 1,
                ExerciseCategoryId = 4,
                DifficultyLevelId = 1,
                Instructions = "Clique em Tocar para ouvir uma nota. Clique em Gravar, cante a nota numa vogal como \"á\" e depois clique em Validar. Pode cantar em qualquer oitava.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Cantarole a nota baixinho antes de gravar e depois sustente-a firme por um ou dois segundos.",
                    "Quando você acerta, o resultado mostra o quanto chegou perto da altura exata, em cents: cem cents formam um semitom.",
                    "Se uma nota for aguda ou grave demais para sua voz, cante-a uma oitava abaixo ou acima, ou limite as oitavas nos filtros.",
                    "Sua gravação é analisada no seu navegador e nunca sai do seu dispositivo."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "SingInterval",
                Description = "Cante a nota que você ouve e depois o intervalo a partir dela.",
                ExerciseTypeId = 3,
                ExerciseCategoryId = 4,
                DifficultyLevelId = 2,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    // The intervals of each level (MusicTheoryService.SingIntervalLevels).
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Level",
                        Name = "siLevel",
                        Options = new List<FilterOption>
                        {
                            new("easy", "Exercise.SingIntervalLevel.Easy"),
                            new("medium", "Exercise.SingIntervalLevel.Medium"),
                            new("all", "Exercise.SingIntervalLevel.All")
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
                Instructions = "Clique em Tocar para ouvir uma nota e ver um intervalo. Clique em Gravar, cante a nota e depois a nota que fica a esse intervalo acima ou abaixo dela; então clique em Validar. Pode cantar em qualquer oitava.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Cante mentalmente a escala a partir da nota e pare no grau do intervalo: a quinta é a quinta nota.",
                    "Respire rapidamente entre as duas notas e sustente cada uma por um segundo.",
                    "O filtro Nível escolhe os intervalos, e o filtro Direção se eles sobem ou descem.",
                    "Sua gravação é analisada no seu navegador e nunca sai do seu dispositivo."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
            },
            new Exercise {
                Name = "SingMelody",
                Description = "Ouça uma melodia curta e repita-a cantando.",
                ExerciseTypeId = 5,
                ExerciseCategoryId = 2,
                DifficultyLevelId = 2,
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
                Instructions = "Clique em Tocar para ouvir uma melodia curta. Clique em Gravar, cante a melodia e depois clique em Validar. Pode cantar em qualquer oitava.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Ouça quantas vezes precisar antes de gravar: a melodia anda por graus e terças e termina na tônica.",
                    "Cante cada nota numa sílaba própria, como \"lá\", para as notas não se misturarem.",
                    "A partir de quatro notas, uma nota errada, faltando ou a mais é perdoada.",
                    "Sua gravação é analisada no seu navegador e nunca sai do seu dispositivo."
                }),
                AudioButtonsJson = JsonConvert.SerializeObject(new List<string>
                {
                    "Play",
                    "Replay"
                }),
                AnswerButtonsJson = JsonConvert.SerializeObject(new Dictionary<string, Dictionary<string, string>>())
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

    /// <summary>
    /// How GuessInterval and GuessFullInterval play their two notes: one after the other (the
    /// page's default, and what plays without the filter), together, or either, drawn for each
    /// round (MusicTheoryService.PlaysTogether).
    /// </summary>
    private static FilterOptionGroup IntervalModeFilter() => new()
    {
        Label = "Exercise.IntervalMode",
        Name = "intervalMode",
        Options = new List<FilterOption>
        {
            new("melodic", "Exercise.IntervalMode.Melodic"),
            new("harmonic", "Exercise.IntervalMode.Harmonic"),
            new("both", "Exercise.IntervalMode.Both")
        }
    };

    /// <summary>
    /// One row per <see cref="AcademiaAuditiva.Services.Gamification.BadgeCatalog"/> entry. The
    /// Portuguese texts predate the resource files and are only stored for reference.
    /// </summary>
    public static List<Badge> BadgeSeed() =>
    [
        // Categoria
        new Badge { BadgeKey = "master_chords", Title = "Mestre dos Acordes", Description = "90% em 3 exercícios de acordes" },
        new Badge { BadgeKey = "sharp_listener", Title = "Ouvinte Afiado", Description = "90% em 3 de percepção" },
        new Badge { BadgeKey = "rhythm_maestro", Title = "Maestro do Ritmo", Description = "100% em 2 de ritmo" },
        new Badge { BadgeKey = "melody_explorer", Title = "Explorador Melódico", Description = "80% em todos os exercícios de melodia" },
        new Badge { BadgeKey = "scale_climber", Title = "Escalador de Tons", Description = "Usou todos os tipos de escalas" },
        new Badge { BadgeKey = "perfect_session", Title = "Sessão Perfeita", Description = "Sessão de pelo menos 10 respostas sem nenhum erro" },

        // Esforço
        new Badge { BadgeKey = "3_days", Title = "3 Dias Seguidos", Description = "Praticou 3 dias consecutivos" },
        new Badge { BadgeKey = "5_days", Title = "5 Dias Seguidos", Description = "Praticou 5 dias consecutivos" },
        new Badge { BadgeKey = "7_days", Title = "7 Dias Seguidos", Description = "Praticou 7 dias consecutivos" },
        new Badge { BadgeKey = "30_days", Title = "30 Dias Seguidos", Description = "Praticou 30 dias consecutivos" },
        new Badge { BadgeKey = "marathon_20min", Title = "Maratona 20min", Description = "20 minutos sem parar" },
        new Badge { BadgeKey = "faithful_practitioner", Title = "Praticante Fiel", Description = "Completou 30 sessões" },
        new Badge { BadgeKey = "100_sessions", Title = "Disco de Ouro", Description = "Completou 100 sessões" },
        new Badge { BadgeKey = "explorer", Title = "Explorador", Description = "Respondeu a todos os exercícios (menos os de canto)" },
        new Badge { BadgeKey = "filter_ninja", Title = "Filtro Ninja", Description = "Usou filtros personalizados em 5 sessões" },
        new Badge { BadgeKey = "first_session", Title = "Iniciador de Jornada", Description = "Primeira sessão realizada" },
        new Badge { BadgeKey = "10_sessions_week", Title = "10 Sessões em 1 Semana", Description = "Alta frequência semanal" },
        new Badge { BadgeKey = "daily_challenge_complete", Title = "Desafio Diário Completo", Description = "Completou um desafio do dia" },

        // Evolução
        new Badge { BadgeKey = "comeback_kid", Title = "Deu a Volta por Cima", Description = "Começou errando e depois passou de 80%" },
        new Badge { BadgeKey = "advanced_conqueror", Title = "Conquistador Avançado", Description = "5 exercícios de nível avançado" },
        new Badge { BadgeKey = "persistent_student", Title = "Aluno Persistente", Description = "Melhorou pontuação em 3 tentativas seguidas" },
        new Badge { BadgeKey = "total_mastery", Title = "Domínio Total", Description = "Completou a trilha de aprendizado" },
        new Badge { BadgeKey = "notable_progress", Title = "Evolução Notável", Description = "Melhorou em todas as categorias em 1 mês" },
        new Badge { BadgeKey = "resilient_ear", Title = "Resiliência Auditiva", Description = "Acertou após 3 erros seguidos" },
        new Badge { BadgeKey = "interval_tamer", Title = "Domador de Intervalos", Description = "10 sessões de intervalos com +80%" },

        // Diversão
        new Badge { BadgeKey = "mission_addict", Title = "Viciado em Desafios", Description = "Completou 10 desafios do dia" },
        new Badge { BadgeKey = "speedster", Title = "Velocista", Description = "Acertou 18 de 20 respostas seguidas em até 4 minutos" },
        new Badge { BadgeKey = "mystery_listener", Title = "Ouvinte Misterioso", Description = "Acertou 5 notas seguidas em Adivinhe a Nota" },
        new Badge { BadgeKey = "impossible_melody", Title = "Melodia Impossível", Description = "Acertou 3 ditados melódicos seguidos" },
        new Badge { BadgeKey = "all_rounder", Title = "Músico Completo", Description = "10 respostas em cada categoria de exercício" },
        new Badge { BadgeKey = "night_owl", Title = "Coruja da Noite", Description = "Começou uma sessão entre 22h e 5h" },
        new Badge { BadgeKey = "early_bird", Title = "Madrugador", Description = "Começou uma sessão entre 5h e 7h" },
        new Badge { BadgeKey = "badge_collector", Title = "Colecionador de Badges", Description = "Obteve 15 conquistas" },
    ];
}
