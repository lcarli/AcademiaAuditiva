document.addEventListener("DOMContentLoaded", () => {
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;

	let currentDegree = "";
	let note1 = null;
	let note2 = null;
	let selectedGuess = "";
	let exerciseStartTime = Date.now();

	const guessButtons = document.querySelectorAll(".guessInterval");

	// Captura clique nas respostas
	guessButtons.forEach(button => {
		button.addEventListener("click", (e) => {
			guessButtons.forEach(btn => btn.classList.remove("selected"));
			e.target.classList.add("selected");
			selectedGuess = e.target.dataset.interval;
		});
	});

	// Gera um novo grau aleatório (2ª, 3ª, ...)
	function getRandomDegree() {
		const degrees = ["2ª", "3ª", "4ª", "5ª", "6ª", "7ª", "8ª"];
		return degrees[Math.floor(Math.random() * degrees.length)];
	}

	// Prepara o intervalo com base nos filtros
	function prepareInterval() {
		const tonic = document.getElementById("keySelect")?.value || "C";
		const scaleType = document.getElementById("scaleTypeSelect")?.value || "major";

		currentDegree = getRandomDegree();

		const scaleNotes = TheoryUtils.getScaleNotes(tonic, scaleType);

		// Extrai o número (ex: "2ª" → 2)
		const degreeNumber = parseInt(currentDegree);

		if (!degreeNumber || degreeNumber > scaleNotes.length) {
			console.warn("Intervalo inválido: " + currentDegree);
			return;
		}

		note1 = scaleNotes[0];
		note2 = scaleNotes[degreeNumber - 1];
	}

	// Botões de áudio
	document.getElementById("playNote1").addEventListener("click", () => {
		if (!note1) prepareInterval();
		if (note1) AudioEngine.playNote(note1, 1);
	});

	document.getElementById("playNote2").addEventListener("click", () => {
		if (!note2) prepareInterval();
		if (note2) AudioEngine.playNote(note2, 1);
	});

	document.getElementById("playInterval").addEventListener("click", () => {
		prepareInterval();
		if (note1 && note2) AudioEngine.playSequence([note1, note2], 0.6);
	});

	document.getElementById("replayInterval").addEventListener("click", () => {
		if (!note1 || !note2) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}
		AudioEngine.playSequence([note1, note2], 0.4);
	});

	// Validação
	document.getElementById("validateGuess").addEventListener("click", () => {
		const correctCountEl = document.getElementById("correctCount");
		const errorCountEl = document.getElementById("errorCount");

		if (!selectedGuess || !currentDegree) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}

		if (selectedGuess === currentDegree) {
			correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
		} else {
			errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, currentDegree);
		}

		// Envia pontuação
		$.post("/Exercise/GuessIntervalSaveScore", {
			correctCount: correctCountEl.innerText,
			errorCount: errorCountEl.innerText,
			timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
		});

		// Reset
		selectedGuess = "";
		currentDegree = "";
		note1 = null;
		note2 = null;
		guessButtons.forEach(btn => btn.classList.remove("selected"));
	});
});