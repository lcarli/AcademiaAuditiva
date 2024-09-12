const AcademiaAuditiva = {
    sampler: null,
    init: function () {

        this.sampler = new Tone.Sampler(this._generateNoteUrls(), {
            release: 1,
            baseUrl: "https://stacademiaauditiva.blob.core.windows.net/piano-audio/",
        }).toDestination();


        Tone.loaded().then(() => {
            //qualquer modificacao aqui
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
        const diminishedMinorThirds = [3, 3, 3];
        const diminishedMajorThirds = [3, 3, 4];
        const ninth = 14;


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
                    allChords.push({ type: "ninth", notes: getChordNotes(note, majorThirds.concat(ninth)) });
                    allChords.push({ type: "diminishedMinor", notes: getChordNotes(note, diminishedMinorThirds) });
                    allChords.push({ type: "diminishedMajor", notes: getChordNotes(note, diminishedMajorThirds) });

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
                    allChords.push({ type: "ninth", notes: getChordNotes(note, majorThirds.concat(ninth)) });
                    allChords.push({ type: "diminishedMinor", notes: getChordNotes(note, diminishedMinorThirds) });
                    allChords.push({ type: "diminishedMajor", notes: getChordNotes(note, diminishedMajorThirds) });

                }
            }
        }
        return allChords.filter(chord => chord.notes.length >= 3); // Filtramos acordes incompletos
    },

    getHarmonicField: function () {
        const allChords = this.getAllChords();
        const allNotes = this.getAllNotes();

        const majorHarmonicField = ["0-major", "2-minor", "4-minor", "5-major", "7-major", "9-minor", "11-diminishedMinor"];
        const minorHarmonicField = ["0-minor", "2-diminishedMinor", "4-major", "5-minor", "7-minor", "9-major", "11-major"];

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
    },

    getAllScales: function (octave = null) {
        const allNotes = this.getAllNotes();

        // Definindo os intervalos para cada tipo de escala:
        const majorScale = [2, 2, 1, 2, 2, 2, 1];
        const minorScale = [2, 1, 2, 2, 1, 2, 2];
        const majorPentatonic = [2, 2, 3, 2, 3];
        const minorPentatonic = [3, 2, 2, 3, 2];

        // Modos gregos:
        const ionian = majorScale;
        const dorian = [2, 1, 2, 2, 2, 1, 2];
        const phrygian = [1, 2, 2, 2, 1, 2, 2];
        const lydian = [2, 2, 2, 1, 2, 2, 1];
        const mixolydian = [2, 2, 1, 2, 2, 1, 2];
        const aeolian = minorScale;
        const locrian = [1, 2, 2, 1, 2, 2, 2];

        function getScaleNotes(rootNote, intervals) {
            let currentIndex = allNotes.indexOf(rootNote);
            const scale = [rootNote];

            for (const interval of intervals) {
                currentIndex += interval;
                if (currentIndex < allNotes.length) {
                    scale.push(allNotes[currentIndex]);
                } else {
                    return [];
                }
            }

            return scale;
        }

        const allScales = [];
        for (const note of allNotes) {
            if (octave != null) {
                if (note.endsWith(octave.toString())) {
                    allScales.push({ type: "major", notes: getScaleNotes(note, majorScale) });
                    allScales.push({ type: "minor", notes: getScaleNotes(note, minorScale) });
                    allScales.push({ type: "majorPentatonic", notes: getScaleNotes(note, majorPentatonic) });
                    allScales.push({ type: "minorPentatonic", notes: getScaleNotes(note, minorPentatonic) });
                    allScales.push({ type: "ionian", notes: getScaleNotes(note, ionian) });
                    allScales.push({ type: "dorian", notes: getScaleNotes(note, dorian) });
                    allScales.push({ type: "phrygian", notes: getScaleNotes(note, phrygian) });
                    allScales.push({ type: "lydian", notes: getScaleNotes(note, lydian) });
                    allScales.push({ type: "mixolydian", notes: getScaleNotes(note, mixolydian) });
                    allScales.push({ type: "aeolian", notes: getScaleNotes(note, aeolian) });
                    allScales.push({ type: "locrian", notes: getScaleNotes(note, locrian) });
                }
            }
            else {
                allScales.push({ type: "major", notes: getScaleNotes(note, majorScale) });
                allScales.push({ type: "minor", notes: getScaleNotes(note, minorScale) });
                allScales.push({ type: "majorPentatonic", notes: getScaleNotes(note, majorPentatonic) });
                allScales.push({ type: "minorPentatonic", notes: getScaleNotes(note, minorPentatonic) });
                allScales.push({ type: "ionian", notes: getScaleNotes(note, ionian) });
                allScales.push({ type: "dorian", notes: getScaleNotes(note, dorian) });
                allScales.push({ type: "phrygian", notes: getScaleNotes(note, phrygian) });
                allScales.push({ type: "lydian", notes: getScaleNotes(note, lydian) });
                allScales.push({ type: "mixolydian", notes: getScaleNotes(note, mixolydian) });
                allScales.push({ type: "aeolian", notes: getScaleNotes(note, aeolian) });
                allScales.push({ type: "locrian", notes: getScaleNotes(note, locrian) });
            }
        }
        return allScales.filter(scale => scale.notes.length >= 5); // Filtramos escalas incompletas (pelo menos 5 notas)
    },


};

// Inicializa o objeto quando o DOM estiver pronto

// Função para carregar o script Tone.js dinamicamente
function loadToneJs(callback) {
    const script = document.createElement("script");
    script.src = "http://unpkg.com/tone";
    script.onload = callback;
    document.body.appendChild(script);
}

// Inicializa o objeto quando o DOM estiver pronto
document.addEventListener("DOMContentLoaded", function () {
    loadToneJs(() => {
        AcademiaAuditiva.init();
    });
});

