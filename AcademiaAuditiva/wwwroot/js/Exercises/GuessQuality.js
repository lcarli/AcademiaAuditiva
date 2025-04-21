document.addEventListener("DOMContentLoaded", () => {
    //Iniciate Audio
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;
	//Iniciate Variables
	let selectedGuess = "";
	let chordNotes = [];
	let exerciseStartTime = Date.now();

	const exerciseId = document.getElementById("exerciseId")?.value;
	const chordGroupSelect = document.getElementById("chordGroup");
	let chordGroup = chordGroupSelect?.value || "all";

	//Iniciate Click Events
	const guessButtons = document.querySelectorAll(".guessAnswer");
	guessButtons.forEach(button => {
		button.addEventListener("click", (e) => {
			guessButtons.forEach(btn => btn.classList.remove("selected"));
			e.target.classList.add("selected");
			selectedGuess = e.target.value;
		});
	});

	//Inciate Play and Replay Events
	const playBtn = document.getElementById("Play");
	if (playBtn) {
		playBtn.addEventListener("click", () => {
			chordGroup = chordGroupSelect?.value || "all";

			fetch("/Exercise/RequestPlay", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({
					exerciseId: exerciseId,
					filters: { chordGroup: chordGroup }
				})
			})
				.then(resp => resp.json())
				.then(data => {
					chordNotes = data.notes;
					AudioEngine.playChord(chordNotes, 1);
				});
		});
	}

	const replayBtn = document.getElementById("Replay");
	if (replayBtn) {
		replayBtn.addEventListener("click", () => {
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
	}

	//Inciate Validate Event
	const validateBtn = document.getElementById("validateGuess");
	if (validateBtn) {
		validateBtn.addEventListener("click", () => {
			if (!selectedGuess) {
				Swal.fire({
					icon: "warning",
					title: loc.incompleteTitle,
					text: loc.incompleteText
				});
				return;
			}

			fetch("/Exercise/ValidateExercise", {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify({
					exerciseId: exerciseId,
					userGuess: selectedGuess,
					timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
				})
			})
				.then(resp => resp.json())
				.then(data => {
					const correctCountEl = document.getElementById("correctCount");
					const errorCountEl = document.getElementById("errorCount");
					if (data.isCorrect) {
						if (correctCountEl) {
							correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
						}
						Swal.fire("Correct!", "You got it right!", "success");
					} else {
						if (errorCountEl) {
							errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
						}
						Swal.fire("Wrong!", `The correct answer was ${data.answer}.`, "error");
					}

					// Reset
					selectedGuess = "";
					chordNotes = [];
					guessButtons.forEach((btn) => btn.classList.remove("selected"));
				});
		});
	}

	function updateVisibleButtons() {
		const allowed = chordGroup === "both"
			? ["major", "minor"]
			: ["major", "major7", "minor", "minor7", "diminished", "diminished7", "augmented"];
	
		guessButtons.forEach(btn => {
			const value = btn.value;
			btn.style.display = allowed.includes(value) ? "inline-block" : "none";
		});
	}
	
	chordGroupSelect?.addEventListener("change", (e) => {
		chordGroup = e.target.value;
		updateVisibleButtons();
	});
	updateVisibleButtons();
});