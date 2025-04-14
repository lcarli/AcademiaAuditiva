document.addEventListener("DOMContentLoaded", () => {
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;

	let randomChord = "";
	let allChords = [];
	let chordGroup = "all";
	let note = "";
	let type = "";

	const guessButtons = document.querySelectorAll(".guessFunction");
	let selectedGuess = "";

	// Atualiza seleção visual dos botões
	guessButtons.forEach(button => {
		button.addEventListener("click", (e) => {
			guessButtons.forEach(btn => btn.classList.remove("selected"));
			e.target.classList.add("selected");
			selectedGuess = e.target.dataset.function;
		});
	});

	// Filtro do grupo
	const chordGroupSelect = document.getElementById("chordGroup");
	const allGuessButtons = document.querySelectorAll(".guessFunction");

	function updateVisibleButtons() {
		allGuessButtons.forEach(btn => {
			const quality = btn.dataset.function;
			const show =
				chordGroup === "major" ? ["major", "major7"].includes(quality) :
				chordGroup === "minor" ? ["minor", "minor7"].includes(quality) :
				true; // all

			btn.style.display = show ? "inline-block" : "none";
		});
	}

if (chordGroupSelect) {
	chordGroup = chordGroupSelect.value;
	updateVisibleButtons();

	chordGroupSelect.addEventListener("change", (e) => {
		chordGroup = e.target.value;
		updateVisibleButtons();
	});
}


	// Gera lista de acordes possíveis com base no filtro
	function getRandomChord() {
		const rootNotes = TheoryUtils.getAllNotes([3, 4]); // oitavas típicas
		const allQualities = [
			"major", "major7", "minor", "minor7", "diminished", "diminished7"
		];

		const allowedQualities = chordGroup === "major"
			? ["major", "major7"]
			: chordGroup === "minor"
				? ["minor", "minor7"]
				: allQualities;

		allChords = TheoryUtils.getAllChords({
			rootNotes: rootNotes,
			qualities: allowedQualities
		});

		const chord = allChords[Math.floor(Math.random() * allChords.length)];
		return chord;
	}

	// Tocar acorde
	document.getElementById("playChord").addEventListener("click", () => {
		const chord = getRandomChord();
		note = chord.notes[0];
		type = chord.type;
		randomChord = `${note}-${type}`;
		AudioEngine.playChord(chord.notes, 1);
	});

	// Repetir acorde
	document.getElementById("replayChord").addEventListener("click", () => {
		if (!randomChord) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}
		const [root, quality] = randomChord.split("-");
		const chordNotes = allChords.find(ch => ch.notes[0] === root && ch.type === quality)?.notes;
		if (chordNotes) {
			AudioEngine.playChord(chordNotes, 1);
		}
	});

	// Validar resposta
	document.getElementById("validateGuess").addEventListener("click", () => {
		const correctCountEl = document.getElementById("correctCount");
		const errorCountEl = document.getElementById("errorCount");

		if (!selectedGuess || !randomChord) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}

		if (selectedGuess === type) {
			correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
		} else {
			errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, `${note} ${type}`);
		}

		// Salva pontuação
		$.post("/Exercise/GuessQualitySaveScore", {
			correctCount: correctCountEl.innerText,
			errorCount: errorCountEl.innerText,
			timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
		});

		// Reset
		selectedGuess = "";
		randomChord = "";
		note = "";
		type = "";
		guessButtons.forEach(btn => btn.classList.remove("selected"));
	});
});