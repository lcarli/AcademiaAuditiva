
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

    return {
        initSampler,
        playNote,
        playSequence
    };
})();
