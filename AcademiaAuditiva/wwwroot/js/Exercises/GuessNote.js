document.addEventListener("DOMContentLoaded", () => {
    AcademiaAuditiva.init();
    AudioEngine.setupWaveform();

    const loc = document.getElementById("localizer").dataset;

    let randomNote = "";
    let userGuessedNote = "";
    let exerciseStartTime = Date.now();

    const guessButtons = document.querySelectorAll('.guessNote');
    guessButtons.forEach(button => {
        button.addEventListener('click', (e) => {
            userGuessedNote = e.target.getAttribute('data-note');
            guessButtons.forEach(btn => btn.classList.remove('selected'));
            button.classList.add('selected');
        });
    });

    function getRandomNote() {
        const notes = TheoryUtils.getAllNotes([4]);
        return notes[Math.floor(Math.random() * notes.length)];
    }

    document.getElementById('playNote').addEventListener('click', () => {
        randomNote = getRandomNote();
        AcademiaAuditiva.audio.playNote(randomNote, 1);
    });

    document.getElementById('replayNote').addEventListener('click', () => {
        if (randomNote) {
            AcademiaAuditiva.audio.playNote(randomNote, 1);
        } else {
            Swal.fire({
                icon: 'warning',
                title: loc.noteNotSelectedTitle,
                text: loc.noteNotSelectedText
            });
        }
    });

    document.getElementById('validateGuess').addEventListener('click', () => {
        if (!userGuessedNote || !randomNote) {
            Swal.fire({
                icon: 'warning',
                title: loc.incompleteTitle,
                text: loc.incompleteText
            });
            return;
        }

        const correctCountEl = document.getElementById('correctCount');
        const errorCountEl = document.getElementById('errorCount');

        if (userGuessedNote === randomNote) {
            correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
            AcademiaAuditiva.feedback.playSuccessSound(loc.correctMessage, loc.correctMessageText);
        } else {
            errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
            AcademiaAuditiva.feedback.playErrorSound(loc.wrongMessage, loc.wrongMessageText, randomNote);
        }

        $.post("/Exercise/GuessNoteSaveScore", {
            correctCount: correctCountEl.innerText,
            errorCount: errorCountEl.innerText,
            timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000)
        });

        userGuessedNote = "";
        randomNote = "";
    });

    setupWaveform();
});

// WAVEFORM
let waveformCanvas;
let analyser;
let audioContext = Tone.context;

function setupWaveform() {
    const container = document.getElementById("waveform");
    waveformCanvas = document.createElement("canvas");
    waveformCanvas.width = container.clientWidth;
    waveformCanvas.height = container.clientHeight;
    container.appendChild(waveformCanvas);

    analyser = audioContext.createAnalyser();
    analyser.fftSize = 2048;

    Tone.Destination.connect(analyser);
    animateWaveform();
}

function animateWaveform() {
    if (!waveformCanvas || !analyser) return;

    const ctx = waveformCanvas.getContext("2d");
    const bufferLength = analyser.fftSize;
    const dataArray = new Uint8Array(bufferLength);

    function draw() {
        requestAnimationFrame(draw);
        analyser.getByteTimeDomainData(dataArray);

        ctx.fillStyle = "#f2f2f2";
        ctx.fillRect(0, 0, waveformCanvas.width, waveformCanvas.height);

        ctx.lineWidth = 2;
        ctx.strokeStyle = "#007BFF";
        ctx.beginPath();

        const sliceWidth = waveformCanvas.width * 1.0 / bufferLength;
        let x = 0;

        for (let i = 0; i < bufferLength; i++) {
            const v = dataArray[i] / 128.0;
            const y = v * waveformCanvas.height / 2;

            if (i === 0) {
                ctx.moveTo(x, y);
            } else {
                ctx.lineTo(x, y);
            }

            x += sliceWidth;
        }

        ctx.lineTo(waveformCanvas.width, waveformCanvas.height / 2);
        ctx.stroke();
    }

    draw();
}