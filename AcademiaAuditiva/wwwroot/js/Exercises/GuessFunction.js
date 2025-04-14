document.addEventListener("DOMContentLoaded", () => {
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;

	let selectedGuess = "";
	let currentFunction = "";
	let chordNotes = [];
	let key = "C";
	let scaleType = "major";

	// DOM
	const keySelect = document.getElementById("keySelect");
	const scaleTypeSelect = document.getElementById("scaleTypeSelect");
	const guessButtons = document.querySelectorAll(".guessFunction");

	// Atualizar escala/tom
	if (keySelect) {
		key = keySelect.value;
		keySelect.addEventListener("change", (e) => {
			key = e.target.value;
		});
	}
	if (scaleTypeSelect) {
		scaleType = scaleTypeSelect.value;
		scaleTypeSelect.addEventListener("change", (e) => {
			scaleType = e.target.value;
			toggleFunctionButtons(scaleType);
		});
	}

	// Alterna exibição dos botões
	function toggleFunctionButtons(type) {
		const majors = document.querySelectorAll(".major-function");
		const minors = document.querySelectorAll(".minor-function");

		majors.forEach(btn => btn.style.display = type === "major" ? "inline-block" : "none");
		minors.forEach(btn => btn.style.display = type === "minor" ? "inline-block" : "none");
	}

	toggleFunctionButtons(scaleType); // inicial

	// Clique em resposta
	guessButtons.forEach(button => {
		button.addEventListener("click", (e) => {
			guessButtons.forEach(btn => btn.classList.remove("selected"));
			e.target.classList.add("selected");
			selectedGuess = e.target.dataset.function;
		});
	});

	// Tocar acorde (gerar aleatoriamente a função)
	document.getElementById("playChord").addEventListener("click", () => {
		const availableFunctions = scaleType === "major"
			? ["1-major", "2-minor", "3-minor", "4-major", "5-major", "6-minor", "7-diminished"]
			: ["1-minor", "2-diminished", "3-major", "4-minor", "5-minor", "6-major", "7-major"];

		currentFunction = availableFunctions[Math.floor(Math.random() * availableFunctions.length)];

		chordNotes = TheoryUtils.getChordFromFunction(key, scaleType, currentFunction);
		AudioEngine.playChord(chordNotes, 1);
	});

	// Repetir
	document.getElementById("replayChord").addEventListener("click", () => {
		if (!chordNotes || chordNotes.length === 0) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}
		AudioEngine.playChord(chordNotes, 1);
	});

	// Validar resposta
	document.getElementById("validateGuess").addEventListener("click", () => {
		const correctCountEl = document.getElementById("correctCount");
		const errorCountEl = document.getElementById("errorCount");

		if (!selectedGuess || !currentFunction) {
			Swal.fire({
				icon: "warning",
				title: loc.incompleteTitle,
				text: loc.incompleteText
			});
			return;
		}

		if (selectedGuess === currentFunction) {
			correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
		} else {
			errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, currentFunction);
		}

		$.post("/Exercise/GuessFunctionSaveScore", {
			correctCount: correctCountEl.innerText,
			errorCount: errorCountEl.innerText,
			timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
		});

		selectedGuess = "";
		currentFunction = "";
		chordNotes = [];
		guessButtons.forEach(btn => btn.classList.remove("selected"));
	});
});