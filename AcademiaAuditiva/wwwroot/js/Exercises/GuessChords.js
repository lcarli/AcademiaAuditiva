document.addEventListener("DOMContentLoaded", () => {
	AcademiaAuditiva.init();
	AudioEngine.setupWaveform();

	const loc = document.getElementById("localizer").dataset;

	let randomChord = "";
	let allChords = "";
	let octaveRange = "single";
	let chordType = "major";

	const guessQualityContainer = document.getElementById('guessQualityContainer');
	guessQualityContainer.classList.add('hidden');

	function getRandomChord() {
		const rootNotes = (octaveRange === "single")
			? TheoryUtils.getAllNotes([4])
			: (octaveRange === "single3")
				? TheoryUtils.getAllNotes([3])
				: TheoryUtils.getAllNotes([2, 3, 4, 5]);

		const allQualities = [
			"major", "minor", "major7", "minor7", "diminished", "diminished7",
			"ninth", "dominant7", "augmented", "sus2", "sus4", "add9", "add11",
			"add13", "major6", "minor6", "halfDiminished", "diminishedMinor", "diminishedMajor"
		];

		const allowedQualities = (chordType === "major")
			? ["major", "major7", "major6", "add9", "add11", "add13", "dominant7", "augmented", "sus2", "sus4"]
			: (chordType === "minor")
				? ["minor", "minor7", "minor6", "halfDiminished", "diminished", "diminished7"]
				: (chordType === "both")
					? ["major", "minor"]
					: allQualities;

		allChords = TheoryUtils.getAllChords({
			rootNotes: rootNotes,
			qualities: allowedQualities
		});

		const filteredChords = allChords.filter(chord => allowedQualities.includes(chord.type));

		const randomChordObj = filteredChords[Math.floor(Math.random() * filteredChords.length)];
		return { notes: randomChordObj.notes, type: randomChordObj.type };
	}

	document.getElementById('playChord').addEventListener('click', () => {
		const chordObj = getRandomChord();
		randomChord = `${chordObj.notes[0]}-${chordObj.type}`;
		AudioEngine.playChord(chordObj.notes, 1);
	});

	document.getElementById('replayChord').addEventListener('click', () => {
		if (randomChord) {
			const [note, quality] = randomChord.split('-');
			const chordNotes = allChords.find(chord => chord.type === quality && chord.notes[0] === note).notes;
			AudioEngine.playChord(chordNotes, 1);
		} else {
			Swal.fire({
				icon: 'warning',
				title: loc.noteNotSelectedTitle,
				text: loc.noteNotSelectedText
			});
		}
	});

	document.getElementById('octaveRange').addEventListener('change', (e) => {
		octaveRange = e.target.value;
	});

	document.getElementById('chordType').addEventListener('change', (e) => {
		chordType = e.target.value;
		if (chordType === 'both' || chordType === 'all') {
			guessQualityContainer.classList.remove('hidden');
		} else {
			guessQualityContainer.classList.add('hidden');
		}
	});

	const noteButtons = document.querySelectorAll('.guessNote');
	const qualityButtons = document.querySelectorAll('.guessQuality');
	let guessedNote = '';
	let guessedQuality = '';

	noteButtons.forEach(button => {
		button.addEventListener('click', (e) => {
			noteButtons.forEach(btn => btn.classList.remove('selected'));
			e.target.classList.add('selected');
			guessedNote = e.target.getAttribute('data-note');
		});
	});

	qualityButtons.forEach(button => {
		button.addEventListener('click', (e) => {
			qualityButtons.forEach(btn => btn.classList.remove('selected'));
			e.target.classList.add('selected');
			guessedQuality = e.target.getAttribute('data-quality');
		});
	});

	function checkGuess() {
		const correctCountEl = document.getElementById('correctCount');
		const errorCountEl = document.getElementById('errorCount');

		randomChord = randomChord.replace(/\d+/g, '');

		if (chordType === "major" || chordType === "minor") {
			guessedQuality = chordType;
		}

		if (`${guessedNote}-${guessedQuality}` === randomChord) {
			correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
		} else {
			errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
			AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, randomChord);
		}

		guessedNote = "";
		guessedQuality = "";
		randomChord = "";
		noteButtons.forEach(btn => btn.classList.remove('selected'));
		qualityButtons.forEach(btn => btn.classList.remove('selected'));

		$.post("/Exercise/GuessChordsSaveScore", {
			correctCount: correctCountEl.innerText,
			errorCount: errorCountEl.innerText,
			timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
		});
	}

	document.getElementById('validateGuess').addEventListener('click', () => {
		const isSimple = (chordType === "major" || chordType === "minor");

		if (isSimple && guessedNote && randomChord) {
			checkGuess();
		} else if (!isSimple && guessedNote && guessedQuality && randomChord) {
			checkGuess();
		} else {
			Swal.fire({
				icon: 'warning',
				title: loc.incompleteTitle,
				text: isSimple ? loc.incompleteText : loc.incompleteText
			});
		}
	});
});