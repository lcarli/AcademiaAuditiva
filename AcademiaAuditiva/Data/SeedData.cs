using AcademiaAuditiva.Data;
using AcademiaAuditiva.Extensions;
using AcademiaAuditiva.Models;
using Newtonsoft.Json;

public static class SeedData
{
    public static void SeedExercises(ApplicationDbContext context)
    {
        var exercises = new List<Exercise>
        {
            new Exercise {
                Name = "GuessNote",
                Description = "Adivinhe a Nota tocada",
                Type = ExerciseType.NoteRecognition,
                Category = ExerciseCategory.EarTraining,
                Difficulty = DifficultyLevel.Beginner,
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
                Type = ExerciseType.ChordRecognition,
                Category = ExerciseCategory.Harmony,
                Difficulty = DifficultyLevel.Beginner,
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
                Type = ExerciseType.IntervalRecognition,
                Category = ExerciseCategory.EarTraining,
                Difficulty = DifficultyLevel.Beginner,
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
                    "Nota1",
                    "Nota2"
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
                Type = ExerciseType.ChordRecognition,
                Category = ExerciseCategory.Harmony,
                Difficulty = DifficultyLevel.Intermediate,
                FiltersJson = JsonConvert.SerializeObject(new List<FilterOptionGroup>
                {
                    new FilterOptionGroup
                    {
                        Label = "Exercise.Quality.ChordGroup",
                        Name = "chordGroup",
                        Options = new List<FilterOption>
                        {
                            new("major", "Exercise.Major"),
                            new("minor", "Exercise.Minor"),
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
                            { "Maior", "major" },
                            { "Menor", "minor" },
                            { "Diminuto", "diminished" },
                            { "Aumentado", "augmented" }
                        }
                    }
                })
            },
            new Exercise {
                Name = "GuessFunction",
                Description = "Adivinhe a função do acorde dentro do campo harmônico.",
                Type = ExerciseType.FunctionRecognition,
                Category = ExerciseCategory.Harmony,
                Difficulty = DifficultyLevel.Intermediate,
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
                Type = ExerciseType.IntervalRecognition,
                Category = ExerciseCategory.EarTraining,
                Difficulty = DifficultyLevel.Advanced,
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
                            new("desc", "Exercise.Direction.Desc")
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
                    "Nota1",
                    "Nota2"
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
                Type = ExerciseType.MelodyReproduction,
                Category = ExerciseCategory.Melody,
                Difficulty = DifficultyLevel.Beginner,
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
                existing.Type = ex.Type;
                existing.Category = ex.Category;
                existing.Difficulty = ex.Difficulty;
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