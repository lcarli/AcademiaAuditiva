
const Feedback = (() => {
    function playSuccessSound() {
        console.log("Success!");
    }

    function playErrorSound() {
        console.log("Error!");
    }

    function showCorrectAnswer(answer) {
        alert("Resposta correta: " + answer);
    }

    return {
        playSuccessSound,
        playErrorSound,
        showCorrectAnswer
    };
})();
