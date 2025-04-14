
const TheoryUtils = (() => {
    function getAllNotes(octaves = [3, 4, 5]) {
        const allNotes = Object.keys(AudioEngine.generateNoteUrls());
        if (!octaves) return allNotes;
        return allNotes.filter(note => octaves.includes(parseInt(note.slice(-1))));
    }

    function getAllChords(filters = { rootNotes: [], qualities: [] }) {
        const allNotes = TheoryUtils.getAllNotes([2, 3, 4, 5]);

        const INTERVALS = {
            major: [4, 3],
            minor: [3, 4],
            diminished: [3, 3],
            augmented: [4, 4],
            sus2: [2, 5],
            sus4: [5, 2],
            add9: [4, 3, 7],
            add11: [4, 3, 10],
            add13: [4, 3, 14],
            major6: [4, 3, 2],
            minor6: [3, 4, 2],
            major7: { base: [4, 3], seventh: 4 },
            minor7: { base: [3, 4], seventh: 3 },
            dominant7: { base: [4, 3], seventh: 3 },
            halfDiminished: { base: [3, 3], seventh: 3 },
            diminished7: { base: [3, 3], seventh: 2 },
            ninth: [4, 3, 7],
            diminishedMinor: [3, 3, 3],
            diminishedMajor: [3, 3, 4]
        };

        function getChordNotes(root, intervals, seventh = null) {
            let index = allNotes.indexOf(root);
            if (index === -1) return [];

            const chord = [root];
            for (const step of intervals) {
                index += step;
                if (index >= allNotes.length) return [];
                chord.push(allNotes[index]);
            }

            if (seventh !== null) {
                index += seventh;
                if (index >= allNotes.length) return [];
                chord.push(allNotes[index]);
            }

            return chord;
        }

        const result = [];

        filters.rootNotes.forEach(root => {
            filters.qualities.forEach(type => {
                const def = INTERVALS[type];
                if (!def) return;

                if (Array.isArray(def)) {
                    const notes = getChordNotes(root, def);
                    if (notes.length >= 3) result.push({ root, type, notes });
                } else {
                    const notes = getChordNotes(root, def.base, def.seventh);
                    if (notes.length >= 3) result.push({ root, type, notes });
                }
            });
        });

        return result;
    }

    function getAllScales(filters = { rootNotes: [], types: [] }) {
        const allNotes = TheoryUtils.getAllNotes([2, 3, 4, 5]);

        const SCALE_INTERVALS = {
            major: [2, 2, 1, 2, 2, 2, 1],
            minor: [2, 1, 2, 2, 1, 2, 2],
            majorPentatonic: [2, 2, 3, 2, 3],
            minorPentatonic: [3, 2, 2, 3, 2],
            ionian: [2, 2, 1, 2, 2, 2, 1],
            dorian: [2, 1, 2, 2, 2, 1, 2],
            phrygian: [1, 2, 2, 2, 1, 2, 2],
            lydian: [2, 2, 2, 1, 2, 2, 1],
            mixolydian: [2, 2, 1, 2, 2, 1, 2],
            aeolian: [2, 1, 2, 2, 1, 2, 2],
            locrian: [1, 2, 2, 1, 2, 2, 2]
        };

        function getScaleNotes(rootNote, intervals) {
            let index = allNotes.indexOf(rootNote);
            const scale = [rootNote];

            for (const step of intervals) {
                index += step;
                if (index < allNotes.length) {
                    scale.push(allNotes[index]);
                } else {
                    return [];
                }
            }

            return scale;
        }

        const result = [];

        filters.rootNotes.forEach(root => {
            filters.types.forEach(type => {
                const intervals = SCALE_INTERVALS[type];
                if (!intervals) return;

                const notes = getScaleNotes(root, intervals);
                if (notes.length >= 5) result.push({ root, type, notes });
            });
        });

        return result;
    }

    function getRandomFunction() {
        const scaleDegrees = {
            major: ["major", "minor", "minor", "major", "major", "minor", "diminished"],
            minor: ["minor", "diminished", "major", "minor", "minor", "major", "major"]
        };

        const rootScale = TheoryUtils.getAllScales({
            rootNotes: [selectedKey],
            types: [selectedQuality]
        })[0];

        const scaleNotes = rootScale.notes;

        const chords = TheoryUtils.getAllChords({
            rootNotes: scaleNotes,
            qualities: scaleDegrees[selectedQuality]
        });

        // Reforçando a correspondência correta com os graus
        const harmonicField = chords.slice(0, 7).map((chord, index) => ({
            position: index + 1,
            type: chord.type,
            notes: chord.notes
        }));

        const randomIndex = Math.floor(Math.random() * harmonicField.length);
        const randomChord = harmonicField[randomIndex];

        return {
            type: randomChord.type,
            position: randomChord.position,
            notes: randomChord.notes
        };
    }

    function generateMelodyWithRhythm({
        measures = 2,
        timeSignature = "4/4",
        octaves = [3, 4, 5],
        includeRests = true
    } = {}) {
        const durations = [
            { name: "whole", value: 4 },
            { name: "half", value: 2 },
            { name: "quarter", value: 1 },
            { name: "eighth", value: 0.5 },
            { name: "sixteenth", value: 0.25 }
        ];

        const totalBeats = parseInt(timeSignature.split('/')[0]) * measures;
        const allNotes = TheoryUtils.getAllNotes(octaves);
        const melody = [];

        let accumulated = 0;
        while (accumulated < totalBeats) {
            const remaining = totalBeats - accumulated;
            const validDurations = durations.filter(d => d.value <= remaining);
            const chosen = validDurations[Math.floor(Math.random() * validDurations.length)];

            const isRest = includeRests && Math.random() < 0.25; // 25% chance de ser pausa
            const note = isRest
                ? "rest"
                : allNotes[Math.floor(Math.random() * allNotes.length)];

            melody.push({ note, duration: chosen.value, type: isRest ? "rest" : "note" });
            accumulated += chosen.value;
        }

        return melody;
    }

    function getScaleNotes(rootNote, type) {
        const scales = TheoryUtils.getAllScales({
            rootNotes: [rootNote],
            types: [type]
        });
    
        if (scales.length > 0) {
            return scales[0].notes;
        }
    
        return [];
    }    


    return {
        getAllNotes,
        getAllChords,
        getAllScales,
        getRandomFunction,
        generateMelodyWithRhythm,
        getScaleNotes
    };
})();
