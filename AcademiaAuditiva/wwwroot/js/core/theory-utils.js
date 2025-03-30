
const TheoryUtils = (() => {
    function getAllNotes(octaves = [3, 4, 5]) {
        const notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return octaves.flatMap(oct => notes.map(n => n + oct));
    }

    function getAllChords(filters) {
        // filters: { qualities: ["major", "minor"], rootNotes: ["C", "D#"] }
        // simplified demo version
        return filters.rootNotes.flatMap(root => {
            return filters.qualities.map(qual => ({
                root,
                type: qual,
                notes: [root, "E4", "G4"] // mock
            }));
        });
    }

    function getAllScales(filters) {
        // mock implementation
        return [{
            name: "C major",
            notes: ["C4", "D4", "E4", "F4", "G4", "A4", "B4"]
        }];
    }

    return {
        getAllNotes,
        getAllChords,
        getAllScales
    };
})();
