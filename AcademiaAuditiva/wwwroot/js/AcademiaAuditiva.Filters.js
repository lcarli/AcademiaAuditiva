document.addEventListener("DOMContentLoaded", () => {
	const startSlider = document.getElementById("rangeStart");
	const endSlider = document.getElementById("rangeEnd");
	const instrumentButtons = document.querySelectorAll("#instrumentButtons [data-instrument]");

	function updateRangeLabels() {
		let start = parseInt(startSlider.value);
		let end = parseInt(endSlider.value);

		if (start > end) {
			end = start;
			endSlider.value = end;
		}

		document.getElementById("rangeStartLabel").innerText = "C" + start;
		document.getElementById("rangeEndLabel").innerText = "C" + end;

		window.AcademiaAuditiva = window.AcademiaAuditiva || {};
		window.AcademiaAuditiva.noteRange = `C${start}-C${end}`;
		document.cookie = `noteRange=${window.AcademiaAuditiva.noteRange}; path=/`;
	}

	// Until the student moves the sliders, the range starts where the chosen instrument plays
	// the exercise: the guitar starts the exercises about chords on its open chords.
	let rangeMoved = false;

	// The sliders only offer the octaves where the chosen instrument sounds natural.
	function applyInstrumentRange(button) {
		const lowest = parseInt(button.dataset.lowestOctave);
		const highest = parseInt(button.dataset.highestOctave);
		const start = parseInt(button.dataset.startOctave);
		for (const slider of [startSlider, endSlider]) {
			const value = rangeMoved || Number.isNaN(start)
				? Math.min(Math.max(parseInt(slider.value), lowest), highest)
				: start;
			slider.min = lowest;
			slider.max = highest;
			slider.value = value;
		}
	}

	// The server reads the instrument cookie when it plays a round (listed in the privacy policy).
	instrumentButtons.forEach((button) => {
		button.addEventListener("click", () => {
			instrumentButtons.forEach((other) => {
				other.setAttribute("aria-pressed", other === button ? "true" : "false");
			});
			document.cookie = "instrument=" + encodeURIComponent(button.dataset.instrument)
				+ "; path=/; max-age=31536000; samesite=lax" + (location.protocol === "https:" ? "; secure" : "");

			if (startSlider && endSlider) {
				applyInstrumentRange(button);
				updateRangeLabels();
			}
		});
	});

	if (startSlider && endSlider) {
		for (const slider of [startSlider, endSlider]) {
			slider.addEventListener("input", () => {
				rangeMoved = true;
				updateRangeLabels();
			});
		}

		updateRangeLabels();
	}
});
