
const GameEngine = (() => {
    function startGame(type) {
        console.log("Iniciando jogo do tipo:", type);
    }

    function generateChallenge() {
        return ExerciseGenerator.generateNoteExercise({ octaves: [4] });
    }

    function validateAnswer(challenge, userAnswer) {
        return challenge.correctAnswer === userAnswer;
    }

    return {
        startGame,
        generateChallenge,
        validateAnswer
    };
})();
