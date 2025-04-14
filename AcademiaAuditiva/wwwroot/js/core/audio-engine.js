
const AudioEngine = (() => {
    let sampler = null;

    function initSampler() {
        sampler = new Tone.Sampler(generateNoteUrls(), {
            release: 1,
            baseUrl: "https://stacademiaauditiva.blob.core.windows.net/piano-audio/",
        }).toDestination();
    }

    function generateNoteUrls() {
        const notes = ["C", "D", "E", "F", "G", "A", "B"];
        const octaves = [1, 2, 3, 4, 5, 6, 7];
        const noteUrls = {};

        for (const octave of octaves) {
            for (const note of notes) {
                const noteName = note + octave;
                noteUrls[noteName] = noteName + ".mp3";

                // Sustenidos
                if (note !== "E" && note !== "B") {
                    const sharpNoteName = note + "#" + octave;
                    noteUrls[sharpNoteName] = note.replace("#", "s") + octave + ".mp3";
                }
            }
        }
        return noteUrls;
    }

    function playNote(note, duration = "0.5") {
        if (sampler) sampler.triggerAttackRelease(note, duration);
    }

    function playSequence(notes, duration = 0.5, delay = 0.6) {
        if (!sampler) return;
        let time = 0;
        for (let note of notes) {
            Tone.Transport.scheduleOnce(() => {
                playNote(note, duration);
            }, `+${time}`);
            time += delay;
        }
        Tone.Transport.start();
    }

    function playChord(notes, duration = 1) {
        if (!sampler || !notes || notes.length === 0) return;
        notes.forEach(note => {
            sampler.triggerAttackRelease(note, duration);
        });
    }    

    function playMelodyWithRhythm(melody) {
        if (!sampler || !melody || melody.length === 0) return;
    
        let time = 0;
        for (let item of melody) {
            if (item.type === "note") {
                Tone.Transport.scheduleOnce(() => {
                    playNote(item.note, item.duration);
                }, `+${time}`);
            }
            time += item.duration;
        }
    
        Tone.Transport.start();
    }

    // === WAVEFORM VISUAL ===
    let waveformCanvas;
    let analyser;
    let audioContext = Tone.context;

    function setupWaveform(targetId = "waveform") {
        const container = document.getElementById(targetId);
        if (!container) return;

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

            const sliceWidth = waveformCanvas.width / bufferLength;
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


    return {
        initSampler,
        generateNoteUrls,
        playNote,
        playSequence,
        playChord,
        playMelodyWithRhythm,
        setupWaveform
    };
})();
