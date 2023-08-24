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

    getAllChords: function (octave = null) {
        const allNotes = this.getAllNotes();

        const majorThirds = [4, 3];
        const minorThirds = [3, 4];
        const diminishedThirds = [3, 3];
        const majorSeventh = 11;
        const minorSeventh = 10;
        const diminishedSeventh = 9;

        function getChordNotes(rootNote, intervals, seventh = null) {
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

            if (seventh !== null) {
                currentIndex += seventh;
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
            if (octave != null) {
                if (note.endsWith(octave.toString())) {
                    allChords.push({ type: "major", notes: getChordNotes(note, majorThirds) });
                    allChords.push({ type: "minor", notes: getChordNotes(note, minorThirds) });
                    allChords.push({ type: "diminished", notes: getChordNotes(note, diminishedThirds) });
                    allChords.push({ type: "major7", notes: getChordNotes(note, majorThirds, majorSeventh) });
                    allChords.push({ type: "minor7", notes: getChordNotes(note, minorThirds, minorSeventh) });
                    allChords.push({ type: "diminished7", notes: getChordNotes(note, diminishedThirds, diminishedSeventh) });
                }
            }
            else {
                if (!note.endsWith('7')) {
                    allChords.push({ type: "major", notes: getChordNotes(note, majorThirds) });
                    allChords.push({ type: "minor", notes: getChordNotes(note, minorThirds) });
                    allChords.push({ type: "diminished", notes: getChordNotes(note, diminishedThirds) });
                    allChords.push({ type: "major7", notes: getChordNotes(note, majorThirds, majorSeventh) });
                    allChords.push({ type: "minor7", notes: getChordNotes(note, minorThirds, minorSeventh) });
                    allChords.push({ type: "diminished7", notes: getChordNotes(note, diminishedThirds, diminishedSeventh) });
                }
            }
        }
        return allChords.filter(chord => chord.notes.length >= 3); // Filtramos acordes incompletos
    },

    getHarmonicField: function () {
        const allChords = this.getAllChords();
        const allNotes = this.getAllNotes();

        const majorHarmonicField = ["0-major", "2-minor", "4-minor", "5-major", "7-major", "9-minor", "11-diminished"];
        const minorHarmonicField = ["0-minor", "2-diminished", "4-major", "5-minor", "7-minor", "9-major", "11-major"];

        function getHarmonicNotes(rootNote, harmonicField) {
            let currentIndex = allNotes.indexOf(rootNote);
            const chord = [];

            if (harmonicField == 'major') {
                for (const n of majorHarmonicField) {
                    const index = parseInt(n.split('-')[0]);
                    const quality = n.split('-')[1];
                    const test = allChords.filter(chord => chord.type == quality && chord.notes[0] == allNotes[currentIndex + index]);
                    chord.push(test)
                }
            }
            else {
                for (const n of minorHarmonicField) {
                    const index = parseInt(n.split('-')[0]);
                    const quality = n.split('-')[1];
                    const test = allChords.filter(chord => chord.type == quality && chord.notes[0] == allNotes[currentIndex + index]);
                    chord.push(test)
                }
            }

            return chord;
        }

        const returnChords = [];
        for (const note of allNotes) {
            returnChords.push({ type: "major", notes: getHarmonicNotes(note, 'major') });
            returnChords.push({ type: "minor", notes: getHarmonicNotes(note, 'minor') });
        }
        return returnChords.filter(chord => chord.notes.length >= 3); // Filtramos acordes incompletos
    }

};

// Inicializa o objeto quando o DOM estiver pronto
document.addEventListener("DOMContentLoaded", function () {
    AcademiaAuditiva.init();
});
