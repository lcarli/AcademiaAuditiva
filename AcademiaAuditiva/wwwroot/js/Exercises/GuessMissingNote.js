document.addEventListener("DOMContentLoaded", () => {
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;

	let melody1 = [];
	let melody2 = [];
	let selectedAnswer = "";
	let exerciseStartTime = Date.now();

	// Botões de resposta
	const guessButtons = document.querySelectorAll(".guessAnswer");
	guessButtons.forEach(button => {
		button.addEventListener("click", (e) => {
			guessButtons.forEach(btn => btn.classList.remove("selected"));
			e.target.classList.add("selected");
			selectedAnswer = e.target.dataset.answer;
		});
	});

    function maybeRemoveNote(original) {
        const shouldRemove = Math.random() < 0.5;
        if (!shouldRemove) return [...original];

        const copy = [...original];
        const indexToRemove = Math.floor(Math.random() * copy.length);
        copy.splice(indexToRemove, 1);
        return copy;
    }

    function generateMelody() {
        return TheoryUtils.generateMelodyWithRhythm({
            measures: 2,
            timeSignature: "4/4",
            octaves: [3, 4]
        });
    }
    

	document.getElementById("playNewMelodies").addEventListener("click", () => {
		const length = parseInt(document.getElementById("melodyLength")?.value || 5);
		const tonic = document.getElementById("keySelect")?.value || "C";
		const scaleType = document.getElementById("scaleTypeSelect")?.value || "major";

		melody1 = generateMelody();
        melody2 = maybeRemoveNote(melody1);

		AcademiaAuditiva.audio.playMelodyWithRhythm(melody1);
        const totalTime = melody1.reduce((sum, n) => sum + n.duration, 0);

        setTimeout(() => {
            AcademiaAuditiva.audio.playMelodyWithRhythm(melody2);
        }, (totalTime + 1) * 1000);
	});


	document.getElementById("replayMelodies").addEventListener("click", () => {
		if (!melody1.length || !melody2.length) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}
		AudioEngine.playSequence(melody1, 0.35, () => {
			setTimeout(() => {
				AudioEngine.playSequence(melody2, 0.35);
			}, 600);
		});
	});


	document.getElementById("playMelody1").addEventListener("click", () => {
		if (melody1.length) AudioEngine.playSequence(melody1, 0.35);
	});

	document.getElementById("playMelody2").addEventListener("click", () => {
		if (melody2.length) AudioEngine.playSequence(melody2, 0.35);
	});


	document.getElementById("validateGuess").addEventListener("click", () => {
		const correctCountEl = document.getElementById("correctCount");
		const errorCountEl = document.getElementById("errorCount");

		if (!selectedAnswer || !melody1.length || !melody2.length) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}

		const isEqual = JSON.stringify(melody1) === JSON.stringify(melody2);
		const userIsCorrect = (isEqual && selectedAnswer === "same") || (!isEqual && selectedAnswer === "different");

		if (userIsCorrect) {
			correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
		} else {
			errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, isEqual ? "same" : "different");
		}

		$.post("/Exercise/GuessMissingNoteSaveScore", {
			correctCount: correctCountEl.innerText,
			errorCount: errorCountEl.innerText,
			timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
		});

		selectedAnswer = "";
		melody1 = [];
		melody2 = [];
		guessButtons.forEach(btn => btn.classList.remove("selected"));
	});
});