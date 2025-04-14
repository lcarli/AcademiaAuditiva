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
                FiltersJson = JsonConvert.SerializeObject(new {
                    Octaves = new[] { 3, 4, 5 }
                }),
                Instructions = "Ouça a nota tocada e selecione a nota correspondente entre as opções disponíveis.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Ouça mais de uma vez se necessário.",
                    "Tente cantar a nota para comparar com seu registro mental.",
                    "Compare com notas que você conhece bem como Dó ou Lá."
                })
            },
            new Exercise {
                Name = "GuessChords",
                Description = "Reconhecimento de acordes",
                Type = ExerciseType.ChordRecognition,
                Category = ExerciseCategory.Harmony,
                Difficulty = DifficultyLevel.Beginner,
                FiltersJson = JsonConvert.SerializeObject(new {
                    ChordTypes = new[] { "Major", "Minor", "Diminished" },
                    Octaves = new[] { 3, 4 }
                }),
                Instructions = "Ouça o acorde tocado e selecione o tipo de acorde correspondente.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Preste atenção na sensação sonora: maior tende a soar feliz, menor mais triste.",
                    "Compare com acordes de referência se necessário.",
                    "Tente cantar as notas para perceber se há intervalos maiores ou menores."
                })
            },
            new Exercise {
                Name = "GuessInterval",
                Description = "Adivinhe o Intervalo tocado",
                Type = ExerciseType.IntervalRecognition,
                Category = ExerciseCategory.EarTraining,
                Difficulty = DifficultyLevel.Beginner,
                FiltersJson = JsonConvert.SerializeObject(new {
                    Octaves = new[] { 3, 4, 5 }
                }),
                Instructions = "Ouça as duas notas e identifique a distância entre elas (intervalo).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Associe intervalos a músicas conhecidas (ex: terça maior = 'Parabéns pra você').",
                    "Ouça repetidamente e cante as notas.",
                    "Perceba se o som é próximo (segunda) ou espaçado (quinta, oitava...)."
                })
            },
            new Exercise {
                Name = "GuessQuality",
                Description = "Adivinhe a qualidade do acorde tocado.",
                Type = ExerciseType.ChordRecognition,
                Category = ExerciseCategory.Harmony,
                Difficulty = DifficultyLevel.Intermediate,
                FiltersJson = JsonConvert.SerializeObject(new {
                    Qualities = new[] { "Major", "Minor", "Diminished" }
                }),
                Instructions = "Ouça o acorde e determine se ele é maior, menor ou diminuto.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Tente memorizar a sonoridade típica de cada qualidade.",
                    "Acordes diminutos soam mais tensos ou instáveis.",
                    "Compare com acordes simples que você já conhece."
                })
            },
            new Exercise {
                Name = "GuessFunction",
                Description = "Adivinhe a função do acorde dentro do campo harmônico.",
                Type = ExerciseType.FunctionRecognition,
                Category = ExerciseCategory.Harmony,
                Difficulty = DifficultyLevel.Intermediate,
                FiltersJson = JsonConvert.SerializeObject(new {
                    Tones = new[] { "C", "D", "E", "F", "G", "A", "B" }
                }),
                Instructions = "Ouça o acorde dentro de um contexto e identifique sua função (tônica, dominante, subdominante).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Lembre que acordes tônicos tendem a soar resolvidos.",
                    "Dominantes soam como tensão que pede resolução.",
                    "Estude o campo harmônico em diferentes tonalidades."
                })
            },
           new Exercise {
                Name = "GuessFullInterval",
                Description = "Adivinhe o intervalo completo (maior, menor, justo...)",
                Type = ExerciseType.IntervalRecognition,
                Category = ExerciseCategory.EarTraining,
                Difficulty = DifficultyLevel.Advanced,
                FiltersJson = JsonConvert.SerializeObject(new {
                    FullIntervals = Enum.GetNames(typeof(FullIntervalType))
                }),
                Instructions = "Ouça o intervalo e identifique não apenas a distância, mas também a sua qualidade (maior, menor, justo, etc).",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Treine com intervalos simples antes de ir para os compostos.",
                    "Associe sons familiares a cada tipo de intervalo.",
                    "Intervalos justos (como quarta e quinta) têm sonoridade estável."
                })
            },
            new Exercise {
                Name = "GuessMissingNote",
                Description = "Ouça duas melodias e diga se são iguais ou diferentes.",
                Type = ExerciseType.MelodyReproduction,
                Category = ExerciseCategory.Melody,
                Difficulty = DifficultyLevel.Beginner,
                FiltersJson = JsonConvert.SerializeObject(new {
                    MelodyLengths = new[] { 4, 5, 6, 7, 8, 9, 10 }
                }),
                Instructions = "Ouça duas melodias e diga se são iguais ou se houve alguma alteração entre elas.",
                TipsJson = JsonConvert.SerializeObject(new[] {
                    "Preste atenção nas notas centrais da melodia.",
                    "Se não tiver certeza, ouça mais de uma vez.",
                    "Cantar ou batucar a melodia pode ajudar na memorização."
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
                context.Exercises.Update(existing);
            }
        }

        context.SaveChanges();
    }
}