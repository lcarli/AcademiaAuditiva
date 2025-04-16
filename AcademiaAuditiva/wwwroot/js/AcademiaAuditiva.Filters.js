document.addEventListener("DOMContentLoaded", () => {
	const noteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

	function updateRangeLabels() {
		const startSlider = document.getElementById("rangeStart");
		const endSlider = document.getElementById("rangeEnd");

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

	const startSlider = document.getElementById("rangeStart");
	const endSlider = document.getElementById("rangeEnd");

	if (startSlider && endSlider) {
		startSlider.min = 1;
		startSlider.max = 6;
		endSlider.min = 1;
		endSlider.max = 6;

		startSlider.addEventListener("input", updateRangeLabels);
		endSlider.addEventListener("input", updateRangeLabels);

		updateRangeLabels();
	}
});