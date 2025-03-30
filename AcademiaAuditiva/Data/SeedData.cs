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
                context.Exercises.Update(existing);
            }
        }

        context.SaveChanges();
    }
}