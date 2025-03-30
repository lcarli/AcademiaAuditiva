
const ExerciseGenerator = (() => {
    function generateNoteExercise(filters) {
        const notes = TheoryUtils.getAllNotes(filters.octaves);
        const selected = notes[Math.floor(Math.random() * notes.length)];
        return {
            type: "note",
            question: selected,
            correctAnswer: selected,
            playback: () => AudioEngine.playNote(selected)
        };
    }

    function generateIntervalExercise(filters) {
        const baseNote = "C4"; // mock
        const interval = "Major Third"; // mock
        return {
            type: "interval",
            question: [baseNote, "E4"],
            correctAnswer: interval,
            playback: () => AudioEngine.playSequence(["C4", "E4"])
        };
    }

    return {
        generateNoteExercise,
        generateIntervalExercise
    };
})();
