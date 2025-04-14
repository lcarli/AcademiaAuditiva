document.addEventListener("DOMContentLoaded", () => {
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;

	let selectedGuess = "";
	let currentInterval = "";
	let note1 = "";
	let note2 = "";

	let key = "C";
	let direction = "asc";

	// Filtros
	const keySelect = document.getElementById("keySelect");
	const directionSelect = document.getElementById("intervalDirection");

	if (keySelect) {
		key = keySelect.value;
		keySelect.addEventListener("change", (e) => {
			key = e.target.value;
		});
	}

	if (directionSelect) {
		direction = directionSelect.value;
		directionSelect.addEventListener("change", (e) => {
			direction = e.target.value;
		});
	}

	// Seleção de resposta
	const guessButtons = document.querySelectorAll(".guessInterval");
	guessButtons.forEach(button => {
		button.addEventListener("click", (e) => {
			guessButtons.forEach(btn => btn.classList.remove("selected"));
			e.target.classList.add("selected");
			selectedGuess = e.target.dataset.interval;
		});
	});

	function getRandomInterval() {
		const intervals = ["2m", "2M", "3m", "3M", "4J", "4A", "5J", "6m", "6M", "7m", "7M", "8J"];
		return intervals[Math.floor(Math.random() * intervals.length)];
	}

	function getIntervalNotes(root, interval, dir) {
		const scale = TheoryUtils.getScaleNotes(root, "major");
		const midiRoot = TheoryUtils.noteToMidi(root);
		const semitoneMap = {
			"2m": 1, "2M": 2,
			"3m": 3, "3M": 4,
			"4J": 5, "4A": 6,
			"5J": 7,
			"6m": 8, "6M": 9,
			"7m": 10, "7M": 11,
			"8J": 12
		};
		const semitones = semitoneMap[interval] || 0;

		const note1Midi = midiRoot;
		const note2Midi = dir === "asc" ? note1Midi + semitones : note1Midi - semitones;

		return [
			TheoryUtils.midiToNote(note1Midi),
			TheoryUtils.midiToNote(note2Midi)
		];
	}

	// Botões de áudio
	document.getElementById("playInterval").addEventListener("click", () => {
		currentInterval = getRandomInterval();
		const notes = getIntervalNotes(key + "4", currentInterval, direction); // ex: C4 + 3m desc
		[note1, note2] = notes;
		AudioEngine.playSequence([note1, note2], 0.6);
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

	document.getElementById("playNote1").addEventListener("click", () => {
		if (note1) AudioEngine.playNote(note1, 1);
	});

	document.getElementById("playNote2").addEventListener("click", () => {
		if (note2) AudioEngine.playNote(note2, 1);
	});

	// Validação
	document.getElementById("validateGuess").addEventListener("click", () => {
		const correctCountEl = document.getElementById("correctCount");
		const errorCountEl = document.getElementById("errorCount");

		if (!selectedGuess || !currentInterval) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}

		if (selectedGuess === currentInterval) {
			correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
		} else {
			errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, currentInterval);
		}

		$.post("/Exercise/GuessFullIntervalSaveScore", {
			correctCount: correctCountEl.innerText,
			errorCount: errorCountEl.innerText,
			timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
		});

		selectedGuess = "";
		currentInterval = "";
		note1 = "";
		note2 = "";
		guessButtons.forEach(btn => btn.classList.remove("selected"));
	});
});