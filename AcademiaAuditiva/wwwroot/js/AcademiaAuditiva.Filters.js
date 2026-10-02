document.addEventListener("DOMContentLoaded", () => {
	const startSlider = document.getElementById("rangeStart");
	const endSlider = document.getElementById("rangeEnd");
	const instrumentButtons = document.querySelectorAll("#instrumentButtons [data-instrument]");
	const positionButtons = document.querySelectorAll("#positionButtons [data-guitar-position]");
	const positionFilter = document.getElementById("positionFilter");
	const rangeFilter = document.getElementById("rangeFilter");

	// The server reads these cookies when it plays a round (listed in the privacy policy).
	function remember(name, value) {
		document.cookie = name + "=" + encodeURIComponent(value)
			+ "; path=/; max-age=31536000; samesite=lax" + (location.protocol === "https:" ? "; secure" : "");
	}

	// An octave is named by its first note the chosen instrument has: E2 on the guitar, G3 on the violin.
	function octaveLabel(octave) {
		const instrument = document.querySelector('#instrumentButtons [aria-pressed="true"]');
		return instrument && octave === parseInt(instrument.dataset.lowestOctave)
			? instrument.dataset.lowestNote
			: "C" + octave;
	}

	function updateRangeLabels() {
		let start = parseInt(startSlider.value);
		let end = parseInt(endSlider.value);

		if (start > end) {
			end = start;
			endSlider.value = end;
		}

		document.getElementById("rangeStartLabel").innerText = octaveLabel(start);
		document.getElementById("rangeEndLabel").innerText = octaveLabel(end);

		window.AcademiaAuditiva = window.AcademiaAuditiva || {};
		window.AcademiaAuditiva.noteRange = `C${start}-C${end}`;
		document.cookie = `noteRange=${window.AcademiaAuditiva.noteRange}; path=/`;
	}

	// The sliders only offer the octaves where the chosen instrument sounds natural.
	function applyInstrumentRange(button) {
		const lowest = parseInt(button.dataset.lowestOctave);
		const highest = parseInt(button.dataset.highestOctave);
		for (const slider of [startSlider, endSlider]) {
			const value = Math.min(Math.max(parseInt(slider.value), lowest), highest);
			slider.min = lowest;
			slider.max = highest;
			slider.value = value;
		}
	}

	instrumentButtons.forEach((button) => {
		button.addEventListener("click", () => {
			instrumentButtons.forEach((other) => {
				other.setAttribute("aria-pressed", other === button ? "true" : "false");
			});
			remember("instrument", button.dataset.instrument);

			// In the exercises that play chords, the guitar plays them where on its neck the
			// student picks, instead of in the note range.
			if (positionFilter && rangeFilter) {
				const onTheNeck = button.dataset.strummed === "true";
				positionFilter.hidden = !onTheNeck;
				rangeFilter.hidden = onTheNeck;
			}

			if (startSlider && endSlider) {
				applyInstrumentRange(button);
				updateRangeLabels();
			}
		});
	});

	positionButtons.forEach((button) => {
		button.addEventListener("click", () => {
			positionButtons.forEach((other) => {
				other.setAttribute("aria-pressed", other === button ? "true" : "false");
			});
			remember("guitarPosition", button.dataset.guitarPosition);
		});
	});

	if (startSlider && endSlider) {
		for (const slider of [startSlider, endSlider]) {
			slider.addEventListener("input", updateRangeLabels);
		}

		updateRangeLabels();
	}
});
