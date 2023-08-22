const AcademiaAuditiva = {
    sampler: null,
    init: function () {
        this.sampler = new Tone.Sampler(this._generateNoteUrls(), {
            release: 1,
            baseUrl: "https://stacademiaauditiva.blob.core.windows.net/piano-audio/",
        }).toDestination();

        Tone.loaded().then(() => {
            // Qualquer lógica de inicialização adicional pode ser colocada aqui
        });
    },
    _generateNoteUrls: function () {
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
    },

    getAllNotes: function () {
        return Object.keys(this._generateNoteUrls());
    },

    getAllChords: function () {
        const allNotes = this.getAllNotes();
        const majorThirds = [4, 3];
        const minorThirds = [3, 4];

        function getChordNotes(rootNote, intervals) {
            let currentIndex = allNotes.indexOf(rootNote);
            const chord = [rootNote];

            for (const interval of intervals) {
                currentIndex += interval;
                if (currentIndex < allNotes.length) { 
                    chord.push(allNotes[currentIndex]);
                } else {
                    return [];
                }
            }
            return chord;
        }

        const allChords = [];
        for (const note of allNotes) {
            // Para simplificar, não consideramos acordes formados com notas das oitavas 6 e 7
            if (!note.endsWith('6') && !note.endsWith('7')) {
                allChords.push({ type: "major", notes: getChordNotes(note, majorThirds) });
                allChords.push({ type: "minor", notes: getChordNotes(note, minorThirds) });
            }
        }
        return allChords.filter(chord => chord.notes.length === 3); // Filtramos acordes incompletos
    },

    getAllChordsWithinOctave: function (octave) {
        const allNotes = this.getAllNotes();
        const majorThirds = [4, 3];
        const minorThirds = [3, 4];

        function getChordNotes(rootNote, intervals) {
            let currentIndex = allNotes.indexOf(rootNote);
            const chord = [rootNote]; 

            for (const interval of intervals) {
                currentIndex += interval;
                if (currentIndex < allNotes.length) {
                    chord.push(allNotes[currentIndex]);
                } else {
                    return [];
                }
            }
            return chord;
        }

        const allChords = [];
        for (const note of allNotes) {
            // Verificamos se a nota atual é da oitava especificada
            if (note.endsWith(octave.toString())) {
                allChords.push({ type: "major", notes: getChordNotes(note, majorThirds) });
                allChords.push({ type: "minor", notes: getChordNotes(note, minorThirds) });
            }
        }
        return allChords.filter(chord => chord.notes.length === 3); // Filtramos acordes incompletos
    }

};

// Inicializa o objeto quando o DOM estiver pronto
document.addEventListener("DOMContentLoaded", function () {
    AcademiaAuditiva.init();
});
