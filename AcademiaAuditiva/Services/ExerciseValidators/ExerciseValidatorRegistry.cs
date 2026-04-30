using AcademiaAuditiva.Interfaces;

namespace AcademiaAuditiva.Services.ExerciseValidators
{
    /// <summary>
    /// Default registry: indexes every <see cref="IExerciseValidator"/>
    /// registered in DI by <c>ExerciseName</c>. Lookup is case-insensitive
    /// because exercise names are content-keyed seeds, not free-form input.
    /// Returns null on miss so callers can shape an explicit error response.
    /// </summary>
    public sealed class ExerciseValidatorRegistry : IExerciseValidatorRegistry
    {
        private readonly IReadOnlyDictionary<string, IExerciseValidator> _byName;

        public ExerciseValidatorRegistry(IEnumerable<IExerciseValidator> validators)
        {
            _byName = validators
                .GroupBy(v => v.ExerciseName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public IExerciseValidator? Get(string exerciseName)
        {
            if (string.IsNullOrWhiteSpace(exerciseName)) return null;
            return _byName.TryGetValue(exerciseName, out var v) ? v : null;
        }
    }
}
