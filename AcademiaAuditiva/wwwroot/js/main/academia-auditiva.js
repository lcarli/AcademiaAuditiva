
const AcademiaAuditiva = {
    init: () => {
        AudioEngine.initSampler();
        console.log("Academia Auditiva pronta!");
    },
    getRandomMelody: (filters) => {
        return TheoryUtils.getAllNotes(filters.octaves).slice(0, filters.length || 5);
    },
    getChallenge: (type, filters) => {
        if (type === "note") return ExerciseGenerator.generateNoteExercise(filters);
        if (type === "interval") return ExerciseGenerator.generateIntervalExercise(filters);
    },
    games: GameEngine,
    audio: AudioEngine,
    feedback: Feedback
};
