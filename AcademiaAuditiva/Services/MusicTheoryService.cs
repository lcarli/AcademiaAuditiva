using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;

namespace AcademiaAuditiva.Services
{
    /// <summary>
    /// Serviço responsável por toda a lógica de teoria musical.
    /// Aqui você vai encapsular notas, escalas, acordes, melodias etc.
    /// </summary>
    public static class MusicTheoryService
    {
        #region Constantes e Dicionários
        /// <summary>
        /// Representa todas as notas em uma oitava cromática, usando sustenidos por padrão.
        /// </summary>
        private static readonly List<string> ChromaticScaleBase = new()
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        /// <summary>
        /// Dicionário contendo as definições de intervalos (em semitons) para cada tipo de acorde.
        /// Para acordes com sétima, armazenamos o intervalo adicional na propriedade SeventhInterval.
        /// </summary>
        private static readonly Dictionary<string, (List<int> BaseIntervals, int? SeventhInterval)> ChordIntervals
            = new Dictionary<string, (List<int> BaseIntervals, int? SeventhInterval)>
        {
            { "major",           (new List<int>{ 4, 3 }, null) },
            { "minor",           (new List<int>{ 3, 4 }, null) },
            { "diminished",      (new List<int>{ 3, 3 }, null) },
            { "augmented",       (new List<int>{ 4, 4 }, null) },
            { "sus2",            (new List<int>{ 2, 5 }, null) },
            { "sus4",            (new List<int>{ 5, 2 }, null) },
            { "add9",            (new List<int>{ 4, 3, 7 }, null) },
            { "add11",           (new List<int>{ 4, 3, 10 }, null) },
            { "add13",           (new List<int>{ 4, 3, 14 }, null) },
            { "major6",          (new List<int>{ 4, 3, 2 }, null) },
            { "minor6",          (new List<int>{ 3, 4, 2 }, null) },
            { "major7",          (new List<int>{ 4, 3 }, 4) },
            { "minor7",          (new List<int>{ 3, 4 }, 3) },
            { "dominant7",       (new List<int>{ 4, 3 }, 3) },
            { "halfDiminished",  (new List<int>{ 3, 3 }, 4) },
            { "diminished7",     (new List<int>{ 3, 3 }, 3) },
            { "ninth",           (new List<int>{ 4, 3, 7 }, null) },
            { "diminishedMinor", (new List<int>{ 3, 3, 3 }, null) },
            { "diminishedMajor", (new List<int>{ 3, 3, 4 }, null) }
        };

        // The letters of the chords that aren't stacked in thirds, in steps up from the letter of
        // the root (the others take every other letter, C E G B): a sus2 has a second where a triad
        // has its third (C D G), a 6 a sixth where a seventh chord has its seventh (C E G A).
        private static readonly Dictionary<string, int[]> ChordLetterSteps = new()
        {
            ["sus2"] = [0, 1, 4],
            ["sus4"] = [0, 3, 4],
            ["add9"] = [0, 2, 4, 8],
            ["add11"] = [0, 2, 4, 10],
            ["add13"] = [0, 2, 4, 12],
            ["major6"] = [0, 2, 4, 5],
            ["minor6"] = [0, 2, 4, 5],
            ["ninth"] = [0, 2, 4, 8],
        };

        private static readonly string[] Triads = ["major", "minor", "diminished", "augmented"];
        private static readonly string[] SeventhChords = ["major7", "dominant7", "minor7", "halfDiminished", "diminished7"];
        private static readonly string[] SusAndAddedChords = ["sus2", "sus4", "major6", "add9"];

        /// <summary>
        /// The chord qualities of each GuessQuality group, its <c>chordGroup</c> filter. All of them
        /// come in the order of the exercise's answer buttons, and the page shows only those of the
        /// group the student picks.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> QualityGroups { get; } =
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["both"] = ["major", "minor"],
                ["triads"] = Triads,
                ["sevenths"] = SeventhChords,
                ["susAdded"] = SusAndAddedChords,
                ["triadsSevenths"] = [.. Triads, .. SeventhChords],
                ["all"] = [.. Triads, .. SeventhChords, .. SusAndAddedChords],
            };

        /// <summary>
        /// Dicionário contendo intervalos (em semitons) para diferentes tipos de escalas.
        /// </summary>
        private static readonly Dictionary<string, List<int>> ScaleIntervals
            = new Dictionary<string, List<int>>
        {
            { "major",           new List<int> { 2, 2, 1, 2, 2, 2, 1 } },
            { "minor",           new List<int> { 2, 1, 2, 2, 1, 2, 2 } },
            { "harmonicMinor",   new List<int> { 2, 1, 2, 2, 1, 3, 1 } },
            { "majorPentatonic", new List<int> { 2, 2, 3, 2, 3 } },
            { "minorPentatonic", new List<int> { 3, 2, 2, 3, 2 } },
            { "ionian",          new List<int> { 2, 2, 1, 2, 2, 2, 1 } },
            { "dorian",          new List<int> { 2, 1, 2, 2, 2, 1, 2 } },
            { "phrygian",        new List<int> { 1, 2, 2, 2, 1, 2, 2 } },
            { "lydian",          new List<int> { 2, 2, 2, 1, 2, 2, 1 } },
            { "mixolydian",      new List<int> { 2, 2, 1, 2, 2, 1, 2 } },
            { "aeolian",         new List<int> { 2, 1, 2, 2, 1, 2, 2 } },
            { "locrian",         new List<int> { 1, 2, 2, 1, 2, 2, 2 } }
        };
        #endregion


        #region Métodos Principais

        /// <summary>
        /// Retorna todas as notas dentro de certas oitavas. Por exemplo, se <paramref name="octaves"/>
        /// for [3,4,5], retorna C3, C#3, ..., B5.
        /// </summary>
        public static List<string> GetAllNotes(List<int> octaves = null)
        {
            // Por default, se não for passada nenhuma oitava, use [3,4,5].
            if (octaves == null || octaves.Count == 0)
                octaves = new List<int> { 3, 4, 5 };

            var allNotes = new List<string>();

            foreach (var octave in octaves)
            {
                foreach (var noteName in ChromaticScaleBase)
                {
                    // Ex: "C" + 3 => "C3"
                    allNotes.Add(noteName + octave);
                }
            }

            return allNotes;
        }

        private static readonly char[] NoteLetters = { 'C', 'D', 'E', 'F', 'G', 'A', 'B' };

        private static readonly Dictionary<char, int> NaturalSemitone = new()
        {
            ['C'] = 0,
            ['D'] = 2,
            ['E'] = 4,
            ['F'] = 5,
            ['G'] = 7,
            ['A'] = 9,
            ['B'] = 11
        };

        private static bool TryParseNoteParts(string note, out char letter, out string accidental, out int octave)
        {
            letter = '\0';
            accidental = string.Empty;
            octave = 0;

            var match = Regex.Match(note.Trim(), @"^([A-Ga-g])([#b♯♭x]*)(-?\d+)$");
            if (!match.Success || !int.TryParse(match.Groups[3].Value, out octave))
            {
                return false;
            }

            letter = char.ToUpperInvariant(match.Groups[1].Value[0]);
            accidental = match.Groups[2].Value;
            return NaturalSemitone.ContainsKey(letter);
        }

        private static string SpellMidiAsLetter(int midi, char letter, int octave)
        {
            var naturalMidi = (octave + 1) * 12 + NaturalSemitone[letter];
            var diff = midi - naturalMidi;

            while (diff > 6) diff -= 12;
            while (diff < -6) diff += 12;

            var accidental = diff switch
            {
                -2 => "bb",
                -1 => "b",
                0 => string.Empty,
                1 => "#",
                2 => "##",
                _ => string.Empty
            };

            return $"{letter}{accidental}{octave}";
        }

        private static string SpellByLetterOffset(int rootMidi, char rootLetter, int rootOctave, int semitoneOffset, int letterOffset)
        {
            var rootLetterIndex = Array.IndexOf(NoteLetters, rootLetter);
            var totalLetterIndex = rootLetterIndex + letterOffset;
            var targetLetter = NoteLetters[((totalLetterIndex % 7) + 7) % 7];
            var targetOctave = rootOctave + (int)Math.Floor(totalLetterIndex / 7.0);
            return SpellMidiAsLetter(rootMidi + semitoneOffset, targetLetter, targetOctave);
        }

        // The note (D#4, Ebb3…) moved by octaves, spelled as it is.
        private static string ShiftOctaves(string note, int octaves) =>
            TryParseNoteParts(note, out var letter, out var accidental, out var octave)
                ? string.Create(CultureInfo.InvariantCulture, $"{letter}{accidental}{octave + octaves}")
                : throw new ArgumentException($"Invalid note name '{note}'.", nameof(note));

        private static IReadOnlyList<int> ScaleLetterOffsets(string scaleType, int count)
        {
            var offsets = scaleType switch
            {
                "majorPentatonic" => new[] { 0, 1, 2, 4, 5, 7 },
                "minorPentatonic" => new[] { 0, 2, 3, 4, 6, 7 },
                _ => new[] { 0, 1, 2, 3, 4, 5, 6, 7 }
            };

            return offsets.Take(count).ToArray();
        }

        /// <summary>Lowest octave a client-supplied <c>noteRange</c> may select (matches the UI slider).</summary>
        public const int MinRangeOctave = 1;

        /// <summary>Highest octave a client-supplied <c>noteRange</c> may select (matches the UI slider).</summary>
        public const int MaxRangeOctave = 6;

        /// <summary>Octave of a missing or malformed <c>noteRange</c>, and where the UI sliders start.</summary>
        public const int DefaultRangeOctave = 4;

        // The exercises whose notes GenerateNoteForExercise draws from the noteRange filter.
        private static readonly HashSet<string> NoteRangeExercises =
            ["GuessNote", "HigherOrLower", "GuessTuning", "GuessChords", "GuessCadence", "GuessInversion", "GuessFunction", "GuessQuality", "GuessDegree", "GuessProgression", "GuessTopNote", "SingNote", "SingInterval"];

        /// <summary>
        /// Whether the notes of <paramref name="exerciseName"/> come from the <c>noteRange</c>
        /// filter: only then does its page offer the octave range sliders. The other exercises
        /// pick their octaves themselves or with a filter of their own (CompleteChord's
        /// <c>ccOctave</c>, for instance).
        /// </summary>
        public static bool UsesNoteRange(string exerciseName) => NoteRangeExercises.Contains(exerciseName);

        /// <summary>
        /// How many cents off the second note of GuessTuning may be, one per level of its
        /// <c>gtLevel</c> filter, the easiest first: a quarter tone down to a twentieth of a semitone.
        /// </summary>
        public static readonly IReadOnlyList<int> TuningLevels = [50, 25, 10, 5];

        /// <summary>The answers of GuessTuning, each drawn as often as the others.</summary>
        public static readonly string[] TuningAnswers = ["inTune", "sharp", "flat"];

        /// <summary>The semitones of each interval code up to the octave, "4A" for the tritone.</summary>
        public static readonly IReadOnlyDictionary<string, int> IntervalSemitones = new Dictionary<string, int>
        {
            ["2m"] = 1, ["2M"] = 2, ["3m"] = 3, ["3M"] = 4, ["4J"] = 5, ["4A"] = 6,
            ["5J"] = 7, ["6m"] = 8, ["6M"] = 9, ["7m"] = 10, ["7M"] = 11, ["8J"] = 12,
        };

        /// <summary>
        /// The intervals SingInterval asks at each level of its <c>siLevel</c> filter: the major
        /// and perfect ones that are easiest to sing, then the minor ones and the sixths, then
        /// all of them. Without the filter, the easy level.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> SingIntervalLevels = new Dictionary<string, string[]>
        {
            ["easy"] = ["2M", "3M", "4J", "5J", "8J"],
            ["medium"] = ["2m", "2M", "3m", "3M", "4J", "5J", "6m", "6M", "8J"],
            ["all"] = ["2m", "2M", "3m", "3M", "4J", "4A", "5J", "6m", "6M", "7m", "7M", "8J"],
        };

        /// <summary>
        /// Parses a <c>noteRange</c> filter such as <c>C3-C5</c> into the list of octaves it spans.
        /// The value comes from the request body/cookie, so malformed input falls back to
        /// <paramref name="defaultOctave"/> and bounds are clamped to [<see cref="MinRangeOctave"/>,
        /// <see cref="MaxRangeOctave"/>] to keep the generated note list small.
        /// </summary>
        public static List<int> ParseOctaveRange(string? noteRange, int defaultOctave = DefaultRangeOctave)
        {
            var parts = noteRange?.Split('-');
            if (parts is not { Length: 2 }
                || !TryParseRangeOctave(parts[0], out var start)
                || !TryParseRangeOctave(parts[1], out var end))
            {
                return new List<int> { defaultOctave };
            }

            if (start > end)
                (start, end) = (end, start);

            start = Math.Clamp(start, MinRangeOctave, MaxRangeOctave);
            end = Math.Clamp(end, MinRangeOctave, MaxRangeOctave);
            return Enumerable.Range(start, end - start + 1).ToList();
        }

        private static bool TryParseRangeOctave(string bound, out int octave)
        {
            octave = 0;
            return bound.Length >= 2
                && char.IsLetter(bound[0])
                && int.TryParse(bound.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out octave);
        }

        /// <summary>
        /// Whether a note of <paramref name="notes"/> is higher than the highest sample (B7): chords
        /// built up from a root in the top octave of the range are then played an octave lower.
        /// </summary>
        private static bool AboveTheSamples(IEnumerable<string> notes) =>
            notes.Any(note => NoteToMidi(note) > PianoSamples.HighestMidi);

        /// <summary>
        /// Retorna todos os acordes com base nos filtros de notas raíz e tipos de acorde.
        /// </summary>
        /// <param name="rootNotes">Lista de notas raíz. (Ex: ["C3", "D3"])</param>
        /// <param name="qualities">Lista de qualidades de acorde. (Ex: ["major", "minor"])</param>
        public static List<(string Root, string Type, List<string> Notes)> GetAllChords(
            List<string> rootNotes,
            List<string> qualities
        )
        {
            var result = new List<(string Root, string Type, List<string> Notes)>();

            if (rootNotes == null || rootNotes.Count == 0)
                return result;

            if (qualities == null || qualities.Count == 0)
                return result;

            // Para cada nota raíz e cada tipo, tentamos montar o acorde.
            foreach (var root in rootNotes)
            {
                foreach (var type in qualities)
                {
                    var chord = GetChordNotes(root, type);
                    if (chord.Count >= 3)
                    {
                        result.Add((root, type, chord));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Retorna todos os conjuntos de notas de escalas com base em notas raíz e tipos de escala.
        /// </summary>
        public static List<(string Root, string Type, List<string> Notes)> GetAllScales(
            List<string> rootNotes,
            List<string> types
        )
        {
            var result = new List<(string Root, string Type, List<string> Notes)>();

            if (rootNotes == null || rootNotes.Count == 0)
                return result;

            if (types == null || types.Count == 0)
                return result;

            // Obtemos todas as notas disponíveis para evitar index out of range (C2...B5, por ex.).
            var allNotes = GetAllNotes(new List<int> { 2, 3, 4, 5 });

            foreach (var root in rootNotes)
            {
                foreach (var type in types)
                {
                    var scale = GetScaleNotes(root, type, allNotes);
                    if (scale.Count >= 5) // Exemplo: escala maior deve ter pelo menos 7 notas
                    {
                        result.Add((root, type, scale));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The melody of a Compare 2 melodies round (GuessMissingNote) or a Which note changed?
        /// round (GuessChangedNote): <paramref name="length"/> notes in a random major key, each a
        /// step or a third from the one before, ending on the tonic. They stay between the fifth
        /// degree below the tonic and the one above it (G3 to G4 in C major), so from G3 to F#5 at
        /// most, which every instrument plays.
        /// </summary>
        /// <returns>
        /// The key's notes in that range (<c>Scale</c>: sol, la and ti below the tonic, then do
        /// to sol) and the melody as indices into them (<c>Melody</c>), so a note can be moved
        /// along the scale.
        /// </returns>
        private static (List<string> Scale, List<int> Melody) GenerateComparisonMelody(int length, Random random)
        {
            var keys = new[] { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
            var key = keys[random.Next(keys.Length)];

            var degrees = GetScaleNotes(key + "3", "major").Skip(4).Take(3)
                .Concat(GetScaleNotes(key + "4", "major").Take(5))
                .ToList();
            var degree = 3; // the tonic

            // Written backwards from the tonic, so that the melody ends on it.
            var melody = new List<int> { degree };
            while (melody.Count < length)
            {
                // A step three times in four, otherwise a third; it turns back at the edges.
                var interval = random.Next(4) == 0 ? 2 : 1;
                var next = random.Next(2) == 0 ? degree - interval : degree + interval;
                degree = next >= 0 && next < degrees.Count ? next : 2 * degree - next;
                melody.Add(degree);
            }

            melody.Reverse();
            return (degrees, melody);
        }

        /// <summary>The notes of a comparison melody, 4 to 8 (SeedData.cs), 5 without the filter.</summary>
        private static int ComparisonMelodyLength(Dictionary<string, string> filters) =>
            filters.TryGetValue("melodyLength", out var raw) && int.TryParse(raw, out var length)
                ? Math.Clamp(length, 4, 8)
                : 5;

        /// <summary>A comparison melody to play: quarter notes, and a half note to end.</summary>
        private static List<object> ComparisonMelodyEntries(List<string> scale, List<int> melody) =>
            [.. melody.Select((degree, i) => (object)new
            {
                type = "note",
                note = scale[degree],
                duration = i == melody.Count - 1 ? 2.0 : 1.0
            })];

        /// <summary>
        /// Gera uma melodia vocal dentro de um certo número de compassos, com time signature, tessitura vocal e nível de dificuldade.
        /// </summary>
        /// <remarks>
        /// Inclui pausas (rests) aleatoriamente com 25% de chance, mas nunca no início.
        /// The notes are natural (C major) and move by at most a third, so the
        /// melody can be sung at sight.
        /// Durações possíveis variam com o nível de dificuldade:
        /// - Fácil: whole (4), half (2), quarter (1)
        /// - Intermediário: + eighth (0.5)
        /// - Avançado: + sixteenth (0.25)
        /// </remarks>
        public static List<(string Note, double Duration, bool IsRest)> GenerateVocalMelody(
            int measures = 2,
            string timeSignature = "4/4",
            string voiceType = "soprano", // baritone, soprano, mezzosoprano, tenor
            string difficulty = "easy", // easy, intermediate, advanced
            bool includeRests = true)
        {
            // Define as oitavas permitidas com base no tipo de voz
            var octaves = voiceType.ToLower() switch
            {
                "baritone" => new List<int> { 2, 3 },
                "tenor" => new List<int> { 3, 4 },
                "mezzosoprano" => new List<int> { 4, 5 },
                "soprano" => new List<int> { 5, 6 },
                _ => new List<int> { 3, 4 } // Default para tenor
            };
        
            // Define as durações possíveis com base no nível de dificuldade
            var rhythmOptions = difficulty.ToLower() switch
            {
                "easy" => new List<(string Name, double Duration, double Weight)>
                {
                    ("whole", 4.0, 0.5),
                    ("half", 2.0, 1.0),
                    ("quarter", 1.0, 2.0)
                },
                "intermediate" => new List<(string Name, double Duration, double Weight)>
                {
                    ("whole", 4.0, 0.5),
                    ("half", 2.0, 1.0),
                    ("quarter", 1.0, 2.0),
                    ("eighth", 0.5, 2.5)
                },
                "advanced" => new List<(string Name, double Duration, double Weight)>
                {
                    ("whole", 4.0, 0.5),
                    ("half", 2.0, 1.0),
                    ("quarter", 1.0, 2.0),
                    ("eighth", 0.5, 2.5),
                    ("sixteenth", 0.25, 2.5)
                },
                _ => new List<(string Name, double Duration, double Weight)>
                {
                    ("whole", 4.0, 0.5),
                    ("half", 2.0, 1.0),
                    ("quarter", 1.0, 2.0)
                }
            };
        
            var allNotes = GetAllNotes(octaves);
            var random = new Random();
            if (allNotes.Count == 0)
            {
                allNotes.Add(ChromaticScaleBase[random.Next(ChromaticScaleBase.Count)] + random.Next(2, 7));
            }
            var singable = allNotes.Where(n => !n.Contains('#')).ToList();
            if (singable.Count == 0)
            {
                singable = allNotes;
            }
            int? previousIndex = null;
        
            var melody = new List<(string Note, double Duration, bool IsRest)>();
            var beatsPerMeasure = int.Parse(timeSignature.Split('/')[0]);
            var totalBeats = beatsPerMeasure * measures;
        
            double accumulated = 0;
            while (accumulated < totalBeats)
            {
                // Filtrar opções que cabem no tempo restante
                var remaining = totalBeats - accumulated;
                var validDurations = rhythmOptions.Where(r => r.Duration <= remaining).ToList();
                if (validDurations.Count == 0)
                    break;
        
                // Seleção com peso
                var totalWeight = validDurations.Sum(r => r.Weight);
                var choice = random.NextDouble() * totalWeight;
                double current = 0;
                (string Name, double Duration, double Weight) selected = validDurations[0];
                foreach (var r in validDurations)
                {
                    current += r.Weight;
                    if (choice <= current)
                    {
                        selected = r;
                        break;
                    }
                }
        
                bool isRest = includeRests && melody.Count > 0 && random.NextDouble() < 0.25;
                string note;
                if (isRest)
                    note = "rest";
                else
                {
                    // Start in the lower octave of the range, then move by a step or a third.
                    var index = previousIndex is int previous
                        ? Math.Clamp(previous + random.Next(-2, 3), 0, singable.Count - 1)
                        : random.Next(Math.Min(singable.Count, 7));
                    previousIndex = index;
                    note = singable[index];
                }
        
                melody.Add((note, selected.Duration, isRest));
                accumulated += selected.Duration;
            }
        
            return melody;
        }
        #endregion


        #region Métodos Auxiliares

        /// <summary>
        /// Retorna a lista de notas da escala baseada em <paramref name="rootNote"/> e <paramref name="scaleType"/>.
        /// O parâmetro <paramref name="allNotes"/> é usado para evitar problemas de index caso queiramos
        /// limitar as notas a certas oitavas.
        /// </summary>
        public static List<string> GetScaleNotes(string rootNote, string scaleType, List<string> allNotes = null)
        {
            if (string.IsNullOrWhiteSpace(rootNote) || string.IsNullOrWhiteSpace(scaleType))
                return new List<string>();

            if (!ScaleIntervals.ContainsKey(scaleType))
                return new List<string>();

            var intervals = ScaleIntervals[scaleType];
            var rootMidi = NoteToMidi(rootNote);
            if (!rootMidi.HasValue || !TryParseNoteParts(rootNote, out var rootLetter, out _, out var rootOctave))
                return new List<string>();

            var semitoneOffsets = new List<int> { 0 };
            var currentOffset = 0;

            foreach (var step in intervals)
            {
                currentOffset += step;
                semitoneOffsets.Add(currentOffset);
            }

            var letterOffsets = ScaleLetterOffsets(scaleType, semitoneOffsets.Count);
            return semitoneOffsets
                .Select((offset, index) => SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, offset, letterOffsets[index]))
                .ToList();
        }

        /// <summary>
        /// Spells the note <paramref name="semitones"/> above <paramref name="root"/> (below when
        /// negative) on the letter <paramref name="letterSteps"/> steps away, so the interval keeps
        /// its name: ("C4", 3, 2) is Eb4, a minor third, and ("C4", -4, -2) is Ab3, a major third
        /// below. Falls back to the sharp name when the letter would need more than a double
        /// accidental. Returns <c>null</c> for an unreadable root.
        /// </summary>
        public static string? SpellInterval(string root, int semitones, int letterSteps)
        {
            var rootMidi = NoteToMidi(root);
            if (!rootMidi.HasValue || !TryParseNoteParts(root, out var rootLetter, out _, out var rootOctave))
                return null;

            var target = rootMidi.Value + semitones;
            var spelled = SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, semitones, letterSteps);
            return NoteToMidi(spelled) == target ? spelled : MidiToNote(target);
        }

        /// <summary>
        /// Retorna as notas de um acorde a partir da nota raíz (<paramref name="root"/>) e da qualidade (<paramref name="quality"/>).
        /// </summary>
        public static List<string> GetChordNotes(string root, string quality)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(quality))
                return result;

            if (!ChordIntervals.ContainsKey(quality))
                return result;

            var (baseIntervals, seventhInterval) = ChordIntervals[quality];


            var rootMidi = NoteToMidi(root);
            if (!rootMidi.HasValue || !TryParseNoteParts(root, out var rootLetter, out _, out var rootOctave))
                return result;

            var letterSteps = ChordLetterSteps.GetValueOrDefault(quality);
            var semitoneOffset = 0;
            var letterOffset = 0;
            var tone = 0;
            result.Add(SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, semitoneOffset, letterOffset));
            foreach (var step in baseIntervals)
            {
                semitoneOffset += step;
                letterOffset = letterSteps?[++tone] ?? letterOffset + 2;
                result.Add(SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, semitoneOffset, letterOffset));
            }

            if (seventhInterval.HasValue)
            {
                semitoneOffset += seventhInterval.Value;
                letterOffset = letterSteps?[++tone] ?? letterOffset + 2;
                result.Add(SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, semitoneOffset, letterOffset));
            }

            return result;
        }

        /// <summary>
        /// Converte o nome da nota (ex: "C#4", "B#4", "Cb5") para o número MIDI correspondente.
        /// </summary>
        public static int? NoteToMidi(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return null;

            var match = Regex.Match(note.Trim(), @"^([A-Ga-g])([#b♯♭x]*)(-?\d+)$");
            if (!match.Success)
                return null;

            var semitone = char.ToUpperInvariant(match.Groups[1].Value[0]) switch
            {
                'C' => 0,
                'D' => 2,
                'E' => 4,
                'F' => 5,
                'G' => 7,
                'A' => 9,
                'B' => 11,
                _ => 0
            };

            foreach (var accidental in match.Groups[2].Value)
            {
                semitone += accidental switch
                {
                    '#' or '♯' => 1,
                    'b' or '♭' => -1,
                    'x' => 2,
                    _ => 0
                };
            }

            if (!int.TryParse(match.Groups[3].Value, out var octave))
                return null;

            // Fórmula: (octave + 1) * 12 + semitone
            // Ex: A4 => 69
            return (octave + 1) * 12 + semitone;
        }

        /// <summary>
        /// Converte um número MIDI para o nome da nota, usando sustenidos.
        /// Exemplo: 60 => C4
        /// </summary>
        public static string MidiToNote(int midiNumber)
        {
            var noteIndex = midiNumber % 12;
            var octave = (midiNumber / 12) - 1;

            if (noteIndex < 0 || noteIndex >= ChromaticScaleBase.Count)
                return null;

            var pitch = ChromaticScaleBase[noteIndex];
            return pitch + octave;
        }

        /// <summary>
        /// Exemplo de método que retorna um acorde funcional (grau) a partir de uma escala.
        /// Ex: Em uma tonalidade C major, o grau I é "C major", grau V é "G major" etc.
        /// </summary>
        /// <param name="key">Nota raiz da tonalidade (ex: "C3")</param>
        /// <param name="scaleType">Tipo de escala (ex: "major")</param>
        /// <param name="functionCode">Código do grau + tipo do acorde, ex: "1-major", "5-dominant7" etc.</param>
        public static List<string> GetChordFromFunction(string key, string scaleType, string functionCode)
        {
            if (string.IsNullOrWhiteSpace(key)
                || string.IsNullOrWhiteSpace(scaleType)
                || string.IsNullOrWhiteSpace(functionCode))
            {
                return new List<string>();
            }

            var degreeMap = new Dictionary<string, int>
            {
                { "1", 0 },
                { "2", 1 },
                { "3", 2 },
                { "4", 3 },
                { "5", 4 },
                { "6", 5 },
                { "7", 6 }
            };

            var parts = functionCode.Split('-');
            if (parts.Length < 2) return new List<string>();

            var degreeStr = parts[0];
            var chordType = parts[1];
            if (!degreeMap.ContainsKey(degreeStr))
                return new List<string>();

            var degreeIndex = degreeMap[degreeStr];

            var scaleNotes = GetScaleNotes(key, scaleType);
            if (degreeIndex < 0 || degreeIndex >= scaleNotes.Count)
                return new List<string>();

            var rootNote = scaleNotes[degreeIndex];

            return GetChordNotes(rootNote, chordType);
        }

        /// <summary>The tonics the key filters offer, spelled with sharps as their options are.</summary>
        private static readonly string[] KeyTonics = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

        /// <summary>The chord of each degree of a major key, as GuessFunction asks them.</summary>
        private static readonly string[] MajorKeyFunctions =
            ["1-major", "2-minor", "3-minor", "4-major", "5-major", "6-minor", "7-diminished"];

        /// <summary>
        /// The chord of each degree of a minor key: the dominant (V) and the leading-tone chord
        /// (vii°) come from the harmonic minor, with the raised seventh, as in tonal music.
        /// </summary>
        private static readonly string[] MinorKeyFunctions =
            ["1-minor", "2-diminished", "3-major", "4-minor", "5-major", "6-major", "7-diminished"];

        /// <summary>
        /// The chord of a degree of a key (a function code of <see cref="GetChordFromFunction"/>),
        /// up from the tonic in <paramref name="octave"/>. A minor key builds it on the harmonic
        /// minor, so 7 is the leading tone (G# in A minor).
        /// </summary>
        /// <param name="key">Tonic, without an octave (ex: "C", "F#").</param>
        /// <param name="scaleType">"minor" for a minor key; anything else is major.</param>
        public static List<string> KeyChord(string key, string scaleType, int octave, string functionCode) =>
            GetChordFromFunction(key + octave, scaleType == "minor" ? "harmonicMinor" : "major", functionCode);

        /// <summary>
        /// The cadence that sets a key before a question about it: I–IV–V–I in a major key and
        /// i–iv–V–i in a minor one, whose dominant has the leading tone, as GuessCadence plays them.
        /// Each chord is in root position, up from the tonic in <paramref name="octave"/>.
        /// </summary>
        /// <param name="key">Tonic, without an octave (ex: "C", "F#").</param>
        /// <param name="scaleType">"minor" for a minor key; anything else is major.</param>
        public static List<List<string>> KeyCadence(string key, string scaleType, int octave)
        {
            string[] functions = scaleType == "minor"
                ? ["1-minor", "4-minor", "5-major", "1-minor"]
                : ["1-major", "4-major", "5-major", "1-major"];
            return [.. functions.Select(function => KeyChord(key, scaleType, octave, function))];
        }

        // The degrees GuessDegree asks, with their semitones above the tonic. They are counted on
        // the key's own scale, the natural minor in a minor key (3, 6 and 7 are then a minor
        // third, sixth and seventh up), and a note out of the scale is a degree of it raised (#)
        // or lowered (b). The answers are the values of the exercise's answer buttons.
        private static readonly (string Degree, int Semitones)[] MajorDiatonicDegrees =
            [("1", 0), ("2", 2), ("3", 4), ("4", 5), ("5", 7), ("6", 9), ("7", 11)];

        private static readonly (string Degree, int Semitones)[] MajorChromaticDegrees =
            [("1", 0), ("b2", 1), ("2", 2), ("b3", 3), ("3", 4), ("4", 5), ("#4", 6), ("5", 7), ("b6", 8), ("6", 9), ("b7", 10), ("7", 11)];

        private static readonly (string Degree, int Semitones)[] MinorDiatonicDegrees =
            [("1", 0), ("2", 2), ("3", 3), ("4", 5), ("5", 7), ("6", 8), ("7", 10)];

        private static readonly (string Degree, int Semitones)[] MinorChromaticDegrees =
            [("1", 0), ("b2", 1), ("2", 2), ("3", 3), ("#3", 4), ("4", 5), ("#4", 6), ("5", 7), ("6", 8), ("#6", 9), ("7", 10), ("#7", 11)];

        // The progressions GuessProgression names, as function codes of GetChordFromFunction on
        // the key's own scale: the natural minor in a minor key, so VII is a whole step below the
        // tonic (G in A minor) while V keeps its major third (E G# B). The names are the values
        // of the exercise's answer buttons; ii°–V–i is "iio-V-i", apart from ii–V–I ignoring case.
        private static readonly (string Name, string[] Chords)[] MajorProgressions =
        [
            ("I-V-vi-IV", ["1-major", "5-major", "6-minor", "4-major"]),
            ("I-vi-IV-V", ["1-major", "6-minor", "4-major", "5-major"]),
            ("ii-V-I", ["2-minor", "5-major", "1-major"]),
            // The 12-bar blues, one bar per chord: I7 I7 I7 I7 | IV7 IV7 I7 I7 | V7 IV7 I7 I7.
            ("blues", ["1-dominant7", "1-dominant7", "1-dominant7", "1-dominant7",
                       "4-dominant7", "4-dominant7", "1-dominant7", "1-dominant7",
                       "5-dominant7", "4-dominant7", "1-dominant7", "1-dominant7"]),
        ];

        private static readonly (string Name, string[] Chords)[] MinorProgressions =
        [
            ("i-VII-VI-V", ["1-minor", "7-major", "6-major", "5-major"]),
            ("i-VI-III-VII", ["1-minor", "6-major", "3-major", "7-major"]),
            ("iio-V-i", ["2-diminished", "5-major", "1-minor"]),
        ];

        // GuessTopNote's chord for each tone it asks on top (the values of its answer buttons):
        // four voices, the root in the bass and three in close position above it, each a tone
        // of the triad (0 the root, 1 the third, 2 the fifth) and its octaves above the bass.
        // In C major: C3 E4 G4 C5, C3 G3 C4 E4 and C3 C4 E4 G4.
        private static readonly (string TopNote, (int Tone, int Octaves)[] Voices)[] TopNoteVoicings =
        [
            ("topRoot", [(0, 0), (1, 1), (2, 1), (0, 2)]),
            ("topThird", [(0, 0), (2, 0), (0, 1), (1, 1)]),
            ("topFifth", [(0, 0), (0, 1), (1, 1), (2, 1)]),
        ];

        // The chords GuessProgression's dictation may move to from each chord of a key, the usual
        // moves of tonal harmony: toward the dominant and back to the tonic, or down by fifths.
        // Its rounds start on the tonic. A minor key's VII is the natural one, as in the
        // progressions above; its V has the leading tone.
        private static readonly Dictionary<string, string[]> MajorProgressionMoves = new()
        {
            ["1-major"] = ["2-minor", "3-minor", "4-major", "5-major", "6-minor"],
            ["2-minor"] = ["5-major", "7-diminished"],
            ["3-minor"] = ["6-minor", "4-major"],
            ["4-major"] = ["1-major", "2-minor", "5-major", "7-diminished"],
            ["5-major"] = ["1-major", "6-minor"],
            ["6-minor"] = ["2-minor", "4-major"],
            ["7-diminished"] = ["1-major"],
        };

        private static readonly Dictionary<string, string[]> MinorProgressionMoves = new()
        {
            ["1-minor"] = ["2-diminished", "3-major", "4-minor", "5-major", "6-major", "7-major"],
            ["2-diminished"] = ["5-major"],
            ["3-major"] = ["6-major", "4-minor"],
            ["4-minor"] = ["1-minor", "5-major", "7-major"],
            ["5-major"] = ["1-minor", "6-major"],
            ["6-major"] = ["2-diminished", "3-major", "4-minor", "7-major"],
            ["7-major"] = ["3-major", "1-minor"],
        };

        /// <summary>
        /// The beats of a round of GuessMeter: a whole number of bars in each of its meters, so
        /// every meter lasts as long and ends on a weak beat.
        /// </summary>
        public const int MeterBeats = 12;

        // The meters GuessMeter asks, with the beats of their bars. The names are the values of
        // the exercise's answer buttons.
        private static readonly (string Meter, int BeatsPerBar)[] Meters = [("duple", 2), ("triple", 3), ("quadruple", 4)];

        // The chords of GuessMeter's accompaniment, one a bar, as the semitones from the tonic up
        // to their roots: I–V–I in the three bars of 4/4, I–IV–V–I in the four of 3/4 and
        // I–IV–V–I–V–I in the six of 2/4, so the chord changes on every first beat.
        private static readonly Dictionary<int, int[]> MeterProgressions = new()
        {
            [4] = [0, 7, 0],
            [3] = [0, 5, 7, 0],
            [2] = [0, 5, 7, 0, 7, 0],
        };

        public static bool NotesAreEquivalent(string note1, string note2)
        {
            // Canonical form keeps the letter capitalized and preserves the
            // accidental (# or b). Stripping octave digits lets callers pass
            // either "C4" or "C". The enharmonic map below is case-sensitive
            // by design — comparisons use OrdinalIgnoreCase so input casing
            // ("c#", "DB", etc.) doesn't break equivalence.
            var enharmonicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "C#", "Db" }, { "Db", "C#" },
                { "D#", "Eb" }, { "Eb", "D#" },
                { "F#", "Gb" }, { "Gb", "F#" },
                { "G#", "Ab" }, { "Ab", "G#" },
                { "A#", "Bb" }, { "Bb", "A#" }
            };

            note1 = Regex.Replace(note1 ?? "", @"\d", "");
            note2 = Regex.Replace(note2 ?? "", @"\d", "");

            if (string.Equals(note1, note2, StringComparison.OrdinalIgnoreCase)) return true;
            if (enharmonicMap.TryGetValue(note1, out var mapped) &&
                string.Equals(mapped, note2, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        public static bool AnswersAreEquivalent(string userAnswer, string correctAnswer)
        {
            if (string.IsNullOrWhiteSpace(userAnswer) || string.IsNullOrWhiteSpace(correctAnswer))
                return false;

            var userParts = userAnswer.Split('|');
            var correctParts = correctAnswer.Split('|');

            if (userParts.Length != correctParts.Length)
                return false;

            switch (userParts.Length)
            {
                case 1:
                    return NotesAreEquivalent(userParts[0], correctParts[0]);

                case 2:
                    return NotesAreEquivalent(userParts[0], correctParts[0]) &&
                           string.Equals(userParts[1], correctParts[1], StringComparison.OrdinalIgnoreCase);

                case 3:
                    return NotesAreEquivalent(userParts[0], correctParts[0]) &&
                           string.Equals(userParts[1], correctParts[1], StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(userParts[2], correctParts[2], StringComparison.OrdinalIgnoreCase);

                default:
                    return string.Equals(userAnswer, correctAnswer, StringComparison.OrdinalIgnoreCase);
            }
        }

        #endregion


        #region Métodos de geração de som por exercicio
        /// <summary>
        /// Whether a round of GuessInterval or GuessFullInterval plays its two notes together, a
        /// harmonic interval, rather than one after the other: as its <c>intervalMode</c> filter
        /// says, <c>melodic</c> (also without the filter), <c>harmonic</c>, or <c>both</c>, drawn
        /// for each round.
        /// </summary>
        private static bool PlaysTogether(Dictionary<string, string> filters, Random random) =>
            filters.GetValueOrDefault("intervalMode") switch
            {
                "harmonic" => true,
                "both" => random.Next(2) == 0,
                _ => false,
            };

        /// <param name="instrument">
        /// Instrument the exercise is played on (the piano when <c>null</c>). The notes drawn one
        /// at a time stay in its note range, and HigherOrLower widens a one-octave
        /// <c>noteRange</c> to its next octave up, or down when this is its top one.
        /// </param>
        public static object GenerateNoteForExercise(Exercise exercise, Dictionary<string, string> filters, Instrument? instrument = null)
        {
            var random = new Random();
            filters.TryGetValue("noteRange", out var noteRange);
            instrument ??= Instrument.Piano;

            switch (exercise.Name)
            {
                case "GuessNote":
                    var allNotes = instrument.NotesIn(instrument.Octaves(noteRange));
                    var selectedNote = allNotes[random.Next(allNotes.Count)];
                    return new { note = selectedNote };

                case "GuessChords":
                    var chordOctaves = ParseOctaveRange(noteRange);

                    var rootNotes = GetAllNotes(chordOctaves);
                    var selectedRoot = rootNotes[random.Next(rootNotes.Count)];

                    var typeFilter = filters.TryGetValue("chordType", out var rawType) ? rawType : "major";
                    List<string> allowedTypes;

                    switch (typeFilter)
                    {
                        case "major":
                            allowedTypes = new List<string> { "major" };
                            break;
                        case "minor":
                            allowedTypes = new List<string> { "minor" };
                            break;
                        case "both":
                            allowedTypes = new List<string> { "major", "minor" };
                            break;
                        case "all":
                        default:
                            allowedTypes = new List<string> { "major", "minor", "diminished", "augmented" };
                            break;
                    }

                    var selectedQuality = allowedTypes[random.Next(allowedTypes.Count)];
                    var chordNotes = GetChordNotes(selectedRoot, selectedQuality);

                    return new
                    {
                        root = Regex.Replace(selectedRoot, @"\d", ""),
                        quality = selectedQuality,
                        notes = chordNotes
                    };

                case "HigherOrLower":
                    var hlOctaves = instrument.Octaves(noteRange);

                    if (hlOctaves.Count < 2)
                    {
                        var octave = hlOctaves[0];
                        hlOctaves.Add(octave < instrument.HighestOctave ? octave + 1 : octave - 1);
                        hlOctaves.Sort();
                    }

                    var hlAllNotes = instrument.NotesIn(hlOctaves);
                    if (hlAllNotes.Count < 2)
                        return new { error = "Not enough notes to compare." };

                    var idxA = random.Next(hlAllNotes.Count);
                    int idxB;
                    do { idxB = random.Next(hlAllNotes.Count); } while (idxB == idxA);

                    var hlNote1 = hlAllNotes[idxA];
                    var hlNote2 = hlAllNotes[idxB];
                    var hlAnswer = idxB > idxA ? "higher" : "lower";

                    return new
                    {
                        note1 = hlNote1,
                        note2 = hlNote2,
                        answer = hlAnswer
                    };

                case "GuessTuning":
                {
                    // The note plays twice: the second time in tune, or the level's cents sharp or flat.
                    var gtNotes = instrument.NotesIn(instrument.Octaves(noteRange));
                    var gtLevel = int.TryParse(filters.GetValueOrDefault("gtLevel"), out var gtParsed) && TuningLevels.Contains(gtParsed)
                        ? gtParsed
                        : TuningLevels[0];
                    var gtAnswer = TuningAnswers[random.Next(TuningAnswers.Length)];

                    return new
                    {
                        note = gtNotes[random.Next(gtNotes.Count)],
                        cents = gtAnswer switch { "sharp" => gtLevel, "flat" => -gtLevel, _ => 0 },
                        answer = gtAnswer
                    };
                }

                case "GuessScaleType":
                    var gstAllRoots = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
                    var gstRoot = filters.TryGetValue("scaleRoot", out var gstK) && gstK != "any" ? gstK : gstAllRoots[random.Next(gstAllRoots.Length)];
                    var gstOctave = filters.TryGetValue("scaleOctave", out var gstOct) && int.TryParse(gstOct, out var gstOctParsed) ? gstOctParsed : 4;

                    var gstScaleTypes = new[] { "major", "minor", "majorPentatonic", "minorPentatonic" };
                    var gstChosen = gstScaleTypes[random.Next(gstScaleTypes.Length)];
                    var gstNotes = GetScaleNotes(gstRoot + gstOctave, gstChosen);
                    if (gstNotes.Count < 2)
                        return new { error = "Escala não pôde ser gerada." };

                    return new
                    {
                        scaleType = gstChosen,
                        notes = gstNotes
                    };

                case "GuessGreekMode":
                    var ggmAllRoots = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
                    var ggmRoot = filters.TryGetValue("scaleRoot", out var ggmK) && ggmK != "any" ? ggmK : ggmAllRoots[random.Next(ggmAllRoots.Length)];
                    var ggmOctave = filters.TryGetValue("scaleOctave", out var ggmOct) && int.TryParse(ggmOct, out var ggmOctParsed) ? ggmOctParsed : 4;

                    var ggmModes = new[] { "ionian", "dorian", "phrygian", "lydian", "mixolydian", "aeolian", "locrian" };
                    var ggmChosen = ggmModes[random.Next(ggmModes.Length)];
                    var ggmNotes = GetScaleNotes(ggmRoot + ggmOctave, ggmChosen);
                    if (ggmNotes.Count < 2)
                        return new { error = "Modo grego não pôde ser gerado." };

                    return new
                    {
                        mode = ggmChosen,
                        notes = ggmNotes
                    };

                case "GuessCadence":
                    var cadAllRoots = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
                    var cadRoot = filters.TryGetValue("cadenceRoot", out var cadK) && cadK != "any" ? cadK : cadAllRoots[random.Next(cadAllRoots.Length)];
                    var cadScale = filters.TryGetValue("cadenceScale", out var cadS) && (cadS == "major" || cadS == "minor") ? cadS : "major";

                    // 4-chord progressions: first 2 set context, last 2 define the cadence.
                    // Mirrors the SonicMind reference catalog.
                    var cadOptions = cadScale == "minor"
                        ? new (string Name, string[] Funcs)[]
                        {
                            ("perfect",   new[] { "1-minor", "4-minor", "5-major", "1-minor" }),
                            ("plagal",    new[] { "1-minor", "5-major", "4-minor", "1-minor" }),
                            ("imperfect", new[] { "1-minor", "6-major", "4-minor", "5-major" }),
                            ("deceptive", new[] { "1-minor", "4-minor", "5-major", "6-major" }),
                        }
                        : new (string Name, string[] Funcs)[]
                        {
                            ("perfect",   new[] { "1-major", "4-major", "5-major", "1-major" }),
                            ("plagal",    new[] { "1-major", "5-major", "4-major", "1-major" }),
                            ("imperfect", new[] { "1-major", "6-minor", "4-major", "5-major" }),
                            ("deceptive", new[] { "1-major", "4-major", "5-major", "6-minor" }),
                        };

                    var cadChosen = cadOptions[random.Next(cadOptions.Length)];
                    var cadOctaves = ParseOctaveRange(noteRange);
                    var cadOctave = cadOctaves[random.Next(cadOctaves.Count)];
                    Func<int, List<List<string>>> cadChordsIn = octave => cadChosen.Funcs
                        .Select(fn => GetChordFromFunction(cadRoot + octave, cadScale, fn))
                        .ToList();
                    var cadChords = cadChordsIn(cadOctave);
                    if (cadChords.Any(AboveTheSamples))
                        cadChords = cadChordsIn(cadOctave - 1);

                    if (cadChords.Any(c => c.Count < 2))
                        return new { error = "Cadência não pôde ser gerada." };

                    return new
                    {
                        cadence = cadChosen.Name,
                        chords = cadChords
                    };

                case "GuessInversion":
                    var invQualityFilter = filters.TryGetValue("invQuality", out var invQf) ? invQf : "both";
                    List<string> invQualities = invQualityFilter switch
                    {
                        "major" => new List<string> { "major" },
                        "minor" => new List<string> { "minor" },
                        _ => new List<string> { "major", "minor" }
                    };

                    var invRootNotes = GetAllNotes(ParseOctaveRange(noteRange));
                    var invRoot = invRootNotes[random.Next(invRootNotes.Count)];
                    var invQuality = invQualities[random.Next(invQualities.Count)];
                    var invChordNotes = GetChordNotes(invRoot, invQuality);

                    if (invChordNotes.Count < 3)
                        return new { error = "Acorde não pôde ser gerado para inversão." };

                    var invInversionTypes = new[] { "root", "first", "second" };
                    var chosenInversion = invInversionTypes[random.Next(invInversionTypes.Length)];

                    var invMidis = invChordNotes.Select(n => NoteToMidi(n) ?? 0).ToList();

                    switch (chosenInversion)
                    {
                        case "first":
                            invMidis[0] += 12;
                            invMidis.Sort();
                            break;
                        case "second":
                            invMidis[0] += 12;
                            invMidis[1] += 12;
                            invMidis.Sort();
                            break;
                    }

                    // An inversion raises notes an octave: in the top octave that can go past the samples.
                    if (invMidis[^1] > PianoSamples.HighestMidi)
                        invMidis = invMidis.Select(midi => midi - 12).ToList();

                    var invFinalNotes = invMidis.Select(MidiToNote).Where(n => n != null).Cast<string>().ToList();

                    return new
                    {
                        inversion = chosenInversion,
                        notes = invFinalNotes
                    };

                case "GuessTopNote":
                {
                    List<string> topNoteQualities = filters.GetValueOrDefault("tnQuality") switch
                    {
                        "major" => ["major"],
                        "minor" => ["minor"],
                        _ => ["major", "minor"],
                    };
                    var topNoteQuality = topNoteQualities[random.Next(topNoteQualities.Count)];
                    var (topNote, topNoteVoices) = TopNoteVoicings[random.Next(TopNoteVoicings.Length)];

                    // The bass is a note of the range; the chord goes down by octaves while its
                    // top is past the instrument's highest note.
                    var topNoteRoots = instrument.NotesIn(instrument.Octaves(noteRange));
                    var topNoteTriad = GetChordNotes(topNoteRoots[random.Next(topNoteRoots.Count)], topNoteQuality);
                    var topNoteNotes = topNoteVoices.Select(voice => ShiftOctaves(topNoteTriad[voice.Tone], voice.Octaves)).ToList();
                    while (NoteToMidi(topNoteNotes[^1]) > instrument.HighestMidi)
                        topNoteNotes = [.. topNoteNotes.Select(note => ShiftOctaves(note, -1))];

                    return new
                    {
                        topNote,
                        quality = topNoteQuality,
                        notes = topNoteNotes
                    };
                }

                case "GuessInterval":
                    var tonic = filters.TryGetValue("keySelect", out var key) ? key : "C4";
                    var scale = filters.TryGetValue("scaleTypeSelect", out var scaleType) ? scaleType : "major";

                    var scaleNotes = GetScaleNotes(tonic, scale);
                    if (scaleNotes.Count < 2)
                        return new { error = "Escala muito curta para gerar intervalo." };

                    var degreeOptions = new[] { 2, 3, 4, 5, 6, 7, 8 };
                    var degree = degreeOptions[random.Next(degreeOptions.Length)];

                    if (degree > scaleNotes.Count)
                        degree = scaleNotes.Count;

                    var note1 = scaleNotes[0];
                    var note2 = scaleNotes[degree - 1];

                    return new
                    {
                        note1,
                        note2,
                        answer = degree.ToString(),
                        harmonic = PlaysTogether(filters, random)
                    };
                case "GuessMissingNote":
                {
                    var melodyLength = ComparisonMelodyLength(filters);
                    var (missingScale, missingDegrees) = GenerateComparisonMelody(melodyLength, random);
                    var melody1 = ComparisonMelodyEntries(missingScale, missingDegrees);

                    // Half the rounds leave out a note, never the first or the last: a beat of the
                    // pulse goes silent, which the student hears. Only the last note is longer.
                    var melody2 = melody1.ToList();
                    var noteLeftOut = random.NextDouble() < 0.5;
                    if (noteLeftOut)
                    {
                        var leftOut = random.Next(1, melodyLength - 1);
                        melody2[leftOut] = new { type = "rest", note = "rest", duration = 1.0 };
                    }

                    return new
                    {
                        melody1,
                        melody2,
                        answer = noteLeftOut ? "diff" : "same"
                    };
                }
                case "GuessChangedNote":
                {
                    var changedLength = ComparisonMelodyLength(filters);
                    var (changedScale, changedDegrees) = GenerateComparisonMelody(changedLength, random);
                    var original = ComparisonMelodyEntries(changedScale, changedDegrees);

                    // Any one note moves along the scale, a step two times in three, otherwise a
                    // third. Past either end of the scale's range it moves the other way instead.
                    var changedAt = random.Next(changedLength);
                    var move = random.Next(3) == 0 ? 2 : 1;
                    var up = random.Next(2) == 0;
                    var movedTo = changedDegrees[changedAt] + (up ? move : -move);
                    if (movedTo < 0 || movedTo >= changedScale.Count)
                    {
                        up = !up;
                        movedTo = changedDegrees[changedAt] + (up ? move : -move);
                    }
                    changedDegrees[changedAt] = movedTo;

                    return new
                    {
                        melody1 = original,
                        melody2 = ComparisonMelodyEntries(changedScale, changedDegrees),
                        // The note's place in the melody, from 1, and where it went.
                        answer = $"{changedAt + 1}|{(up ? "up" : "down")}"
                    };
                }
                case "GuessFullInterval":
                    var tonicNote = filters.TryGetValue("keySelect", out var root) ? root + "4" : "C4";
                    var fullHarmonic = PlaysTogether(filters, random);
                    var direction = filters.TryGetValue("intervalDirection", out var dir) ? dir : "asc";
                    // Two notes played together have no direction: the note of the key is the lower one.
                    if (fullHarmonic)
                        direction = "asc";
                    else if (direction == "both")
                        direction = random.NextDouble() < 0.5 ? "asc" : "desc";

                    var intervalOptions = new Dictionary<string, int>
                    {
                        { "2m", 1 }, { "2M", 2 },
                        { "3m", 3 }, { "3M", 4 },
                        // The answer buttons name the tritone "5d"; the validator also accepts "4A".
                        { "4J", 5 }, { "5d", 6 },
                        { "5J", 7 },
                        { "6m", 8 }, { "6M", 9 },
                        { "7m", 10 }, { "7M", 11 },
                        { "8J", 12 }
                    };

                    var intervalList = intervalOptions.Keys.ToList();
                    var chosenInterval = intervalList[random.Next(intervalList.Count)];
                    var semitones = intervalOptions[chosenInterval];

                    var tonicMidi = NoteToMidi(tonicNote) ?? 60;
                    var secondNoteMidi = direction == "asc" ? tonicMidi + semitones : tonicMidi - semitones;

                    var noteA = MidiToNote(tonicMidi);
                    var noteB = MidiToNote(secondNoteMidi);

                    return new
                    {
                        note1 = noteA,
                        note2 = noteB,
                        answer = chosenInterval,
                        harmonic = fullHarmonic
                    };
                case "GuessFunction":
                {
                    var functionKey = filters.TryGetValue("keySelect", out var k) && KeyTonics.Contains(k) ? k : "C";
                    var functionScale = filters.TryGetValue("scaleTypeSelect", out var s) && s == "minor" ? "minor" : "major";
                    var functionList = functionScale == "minor" ? MinorKeyFunctions : MajorKeyFunctions;
                    var selectedFunction = functionList[random.Next(functionList.Length)];

                    // The cadence sets the key before the chord, in the same octave: an octave
                    // lower when a note of either would go past the highest sample.
                    var functionOctaves = ParseOctaveRange(noteRange);
                    var functionOctave = functionOctaves[random.Next(functionOctaves.Count)];
                    var functionCadence = KeyCadence(functionKey, functionScale, functionOctave);
                    var chordFunc = KeyChord(functionKey, functionScale, functionOctave, selectedFunction);
                    if (AboveTheSamples(chordFunc) || functionCadence.Any(AboveTheSamples))
                    {
                        functionCadence = KeyCadence(functionKey, functionScale, functionOctave - 1);
                        chordFunc = KeyChord(functionKey, functionScale, functionOctave - 1, selectedFunction);
                    }

                    return new
                    {
                        cadence = functionCadence,
                        notes = chordFunc,
                        answer = selectedFunction
                    };
                }
                case "GuessDegree":
                {
                    // "any" draws the key of each question; an unknown key, scale or level is C major, diatonic.
                    filters.TryGetValue("keySelect", out var degreeKeyFilter);
                    var degreeKey = degreeKeyFilter == "any"
                        ? KeyTonics[random.Next(KeyTonics.Length)]
                        : KeyTonics.FirstOrDefault(keyTonic => keyTonic == degreeKeyFilter) ?? "C";
                    var degreeScale = filters.TryGetValue("scaleTypeSelect", out var degreeScaleFilter) && degreeScaleFilter == "minor" ? "minor" : "major";
                    var chromatic = filters.TryGetValue("gdLevel", out var degreeLevelFilter) && degreeLevelFilter == "chromatic";
                    var degrees = (degreeScale, chromatic) switch
                    {
                        ("minor", true) => MinorChromaticDegrees,
                        ("minor", false) => MinorDiatonicDegrees,
                        (_, true) => MajorChromaticDegrees,
                        _ => MajorDiatonicDegrees,
                    };
                    var (degreeAnswer, degreeSemitones) = degrees[random.Next(degrees.Length)];

                    // The cadence sets the key in an octave of the range, an octave lower when a note
                    // of it would go past the highest sample. The note is then the degree up from that
                    // tonic, moved by octaves into the notes of the instrument.
                    var degreeOctaves = instrument.Octaves(noteRange);
                    var degreeOctave = degreeOctaves[random.Next(degreeOctaves.Count)];
                    if (KeyCadence(degreeKey, degreeScale, degreeOctave).Any(AboveTheSamples))
                        degreeOctave--;
                    var degreeMidi = NoteToMidi(degreeKey + degreeOctave)!.Value + degreeSemitones;
                    while (degreeMidi > instrument.HighestMidi)
                        degreeMidi -= 12;
                    while (degreeMidi < instrument.LowestMidi)
                        degreeMidi += 12;

                    return new
                    {
                        cadence = KeyCadence(degreeKey, degreeScale, degreeOctave),
                        note = MidiToNote(degreeMidi),
                        answer = degreeAnswer
                    };
                }
                case "GuessProgression":
                {
                    // "any" draws the key of each question; an unknown key, scale or level is C major, naming.
                    filters.TryGetValue("keySelect", out var progressionKeyFilter);
                    var progressionKey = progressionKeyFilter == "any"
                        ? KeyTonics[random.Next(KeyTonics.Length)]
                        : KeyTonics.FirstOrDefault(keyTonic => keyTonic == progressionKeyFilter) ?? "C";
                    var progressionScale = filters.TryGetValue("scaleTypeSelect", out var progressionScaleFilter) && progressionScaleFilter == "minor" ? "minor" : "major";
                    var dictation = filters.TryGetValue("gpLevel", out var progressionLevelFilter) && progressionLevelFilter == "numerals";

                    string[] progressionFunctions;
                    string progressionAnswer;
                    if (dictation)
                    {
                        // The tonic, then three chords, each a move from the one before: the
                        // student names chords 2 to 4.
                        var moves = progressionScale == "minor" ? MinorProgressionMoves : MajorProgressionMoves;
                        var dictated = new List<string> { progressionScale == "minor" ? "1-minor" : "1-major" };
                        while (dictated.Count < 4)
                        {
                            var nextChords = moves[dictated[^1]];
                            dictated.Add(nextChords[random.Next(nextChords.Length)]);
                        }
                        progressionFunctions = [.. dictated];
                        progressionAnswer = string.Join('|', dictated.Skip(1));
                    }
                    else
                    {
                        var progressions = progressionScale == "minor" ? MinorProgressions : MajorProgressions;
                        (progressionAnswer, progressionFunctions) = progressions[random.Next(progressions.Length)];
                    }

                    // The cadence sets the key, then the progression follows in the same octave: an
                    // octave lower when a note of either would go past the highest sample.
                    var progressionOctaves = ParseOctaveRange(noteRange);
                    var progressionOctave = progressionOctaves[random.Next(progressionOctaves.Count)];
                    Func<int, List<List<string>>> progressionChordsIn = octave => [.. progressionFunctions
                        .Select(function => GetChordFromFunction(progressionKey + octave, progressionScale, function))];
                    var progressionCadence = KeyCadence(progressionKey, progressionScale, progressionOctave);
                    var progressionChords = progressionChordsIn(progressionOctave);
                    if (progressionCadence.Any(AboveTheSamples) || progressionChords.Any(AboveTheSamples))
                    {
                        progressionCadence = KeyCadence(progressionKey, progressionScale, progressionOctave - 1);
                        progressionChords = progressionChordsIn(progressionOctave - 1);
                    }

                    return new
                    {
                        cadence = progressionCadence,
                        chords = progressionChords,
                        answer = progressionAnswer
                    };
                }
                case "GuessMeter":
                {
                    // MeterBeats beats in bars of two, three or four. An unknown level is clicks,
                    // which the planner makes from the beats of a bar.
                    var (meterAnswer, meterBeatsPerBar) = Meters[random.Next(Meters.Length)];
                    if (!(filters.TryGetValue("gmLevel", out var meterLevel) && meterLevel == "accompaniment"))
                        return new { level = "clicks", beatsPerBar = meterBeatsPerBar, answer = meterAnswer };

                    // An oom-pah in a major key drawn for the round: the root of each bar's chord in
                    // the bass, from F2 to E3, then the chord in root position an octave above it.
                    var meterKey = KeyTonics[random.Next(KeyTonics.Length)];
                    var meterTonic = NoteToMidi(meterKey + "4")!.Value % 12;
                    var meterBars = MeterProgressions[meterBeatsPerBar].Select(meterRoot =>
                    {
                        var meterPitchClass = (meterTonic + meterRoot) % 12;
                        var meterBass = meterPitchClass <= 4 ? 48 + meterPitchClass : 36 + meterPitchClass;
                        return new
                        {
                            bass = MidiToNote(meterBass),
                            chord = new[] { meterBass + 12, meterBass + 16, meterBass + 19 }.Select(MidiToNote).ToList()
                        };
                    }).ToList();

                    return new
                    {
                        level = "accompaniment",
                        beatsPerBar = meterBeatsPerBar,
                        key = meterKey,
                        bars = meterBars,
                        answer = meterAnswer
                    };
                }
                case "GuessQuality":
                    var qualityGroup = filters.TryGetValue("chordGroup", out var group) ? group : "all";
                    var allowedQualities = QualityGroups.GetValueOrDefault(qualityGroup) ?? QualityGroups["all"];

                    var rootNotesQ = GetAllNotes(ParseOctaveRange(noteRange));
                    var allChordsQ = GetAllChords(rootNotesQ, [.. allowedQualities]);
                    var chordQ = allChordsQ[random.Next(allChordsQ.Count)];
                    // The ninth of an add9 is past the samples from A#6 up: such a chord is played an octave lower.
                    var notesQ = AboveTheSamples(chordQ.Notes)
                        ? GetChordNotes(MidiToNote(NoteToMidi(chordQ.Root)!.Value - 12), chordQ.Type)
                        : chordQ.Notes;

                    return new
                    {
                        root = Regex.Replace(chordQ.Root, @"\d", ""),
                        type = chordQ.Type,
                        notes = notesQ,
                        answer = chordQ.Type
                    };
                case "SolfegeMelody":
                    // One 4/4 bar in the mezzo-soprano range (C4-B5); students may sing it in any octave.
                    var melodyRawSolfege = GenerateVocalMelody(measures: 1, timeSignature: "4/4", voiceType: "mezzosoprano", difficulty: "easy", includeRests: true);
                
                    var melodySolfege = melodyRawSolfege.Select(m => new
                    {
                        type = m.IsRest ? "rest" : "note",
                        note = m.Note,
                        duration = m.Duration
                    }).ToList();
                
                    return new
                    {
                        melody = melodySolfege
                    };

                // The singing exercises: the student sings back what is played, in any octave.
                case "SingNote":
                {
                    var singNotes = instrument.NotesIn(instrument.Octaves(noteRange));
                    return new { note = singNotes[random.Next(singNotes.Count)] };
                }
                case "SingInterval":
                {
                    // The first note is played; the second is only sung, so it may leave the range.
                    var startNotes = instrument.NotesIn(instrument.Octaves(noteRange));
                    var start = startNotes[random.Next(startNotes.Count)];
                    var intervals = SingIntervalLevels.TryGetValue(filters.GetValueOrDefault("siLevel") ?? "", out var levelIntervals)
                        ? levelIntervals
                        : SingIntervalLevels["easy"];
                    var interval = intervals[random.Next(intervals.Length)];
                    var singDirection = filters.GetValueOrDefault("intervalDirection") switch
                    {
                        "desc" => "desc",
                        "both" => random.Next(2) == 0 ? "asc" : "desc",
                        _ => "asc",
                    };
                    var startMidi = NoteToMidi(start)!.Value;
                    var steps = IntervalSemitones[interval];

                    return new
                    {
                        note1 = start,
                        note2 = MidiToNote(singDirection == "asc" ? startMidi + steps : startMidi - steps),
                        interval,
                        direction = singDirection
                    };
                }
                case "SingMelody":
                {
                    // Like GuessChangedNote's melodies: steps and thirds of a major scale, ending on its tonic.
                    var (singScale, singDegrees) = GenerateComparisonMelody(ComparisonMelodyLength(filters), random);
                    return new { melody = ComparisonMelodyEntries(singScale, singDegrees) };
                }

                case "IntervalMelodico":
                    var keyMel = filters.TryGetValue("keySelect", out var selectedKeyMel) ? selectedKeyMel : "C";
                    var scaleTypeMel = filters.TryGetValue("scaleTypeSelect", out var selectedScale) ? selectedScale : "major";
                    if (scaleTypeMel == "both")
                        scaleTypeMel = random.NextDouble() < 0.5 ? "major" : "minor";

                    // Gera escala base
                    var scaleNotesKey = GetScaleNotes(keyMel + "4", scaleTypeMel);
                    if (scaleNotesKey.Count < 4)
                    {
                        // Unknown key or scale (only possible with a hand-made request).
                        keyMel = "C";
                        scaleTypeMel = "major";
                        scaleNotesKey = GetScaleNotes("C4", "major");
                    }

                    // Melodia com 8-32 notas, pode começar em diferentes graus (não sempre no I)
                    var melodyLengthMel = random.Next(8, 32);
                    var startingDegree = random.Next(1, Math.Min(scaleNotesKey.Count, 6)); // Graus I-V como início
                    var melodyNotes = new List<string>();
                    
                    // Primeira nota baseada no grau inicial
                    var currentDegree = startingDegree;
                    melodyNotes.Add(scaleNotesKey[currentDegree - 1]);
                    
                    // Gera restante da melodia com movimentos aleatórios
                    for (int i = 1; i < melodyLengthMel; i++)
                    {
                        var movement = random.Next(-4, 5); // Movimento de -4 a +4 graus
                        currentDegree = Math.Max(1, Math.Min(scaleNotesKey.Count, currentDegree + movement));
                        melodyNotes.Add(scaleNotesKey[currentDegree - 1]);
                    }

                    // Calcula os graus relativos à tônica
                    var firstNote = melodyNotes[0];
                    var lastNote = melodyNotes[melodyNotes.Count - 1];
                    var firstDegree = GetDegreeInScale(firstNote, scaleNotesKey);
                    var lastDegree = GetDegreeInScale(lastNote, scaleNotesKey);
                    
                    // Calcula intervalos
                    var startInterval = GetIntervalBetweenNotes(melodyNotes[0], melodyNotes[1]);
                    var endInterval = GetIntervalBetweenNotes(melodyNotes[melodyNotes.Count - 2], melodyNotes[melodyNotes.Count - 1]);
                    
                    return new
                    {
                        melody = melodyNotes,
                        firstDegree = firstDegree,
                        lastDegree = lastDegree,
                        startInterval = startInterval,
                        endInterval = endInterval,
                        key = keyMel,
                        scale = scaleTypeMel
                    };
                case "CompleteScale":
                {
                    var allNotesPool = new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B", "Db", "Gb" };
                    var scalePool = new[] { "major", "minor", "majorPentatonic", "minorPentatonic" };
                    var csRoot = filters.TryGetValue("csRoot", out var csR) && allNotesPool.Contains(csR)
                        ? csR
                        : allNotesPool[random.Next(allNotesPool.Length)];
                    var csScale = filters.TryGetValue("csScale", out var csS) && scalePool.Contains(csS)
                        ? csS
                        : scalePool[random.Next(scalePool.Length)];
                    var csOctave = filters.TryGetValue("csOctave", out var csO)
                        && int.TryParse(csO, out var csOctP)
                        && (csOctP == 3 || csOctP == 4)
                        ? csOctP
                        : 4;

                    var csNotes = GetScaleNotes(csRoot + csOctave, csScale);
                    if (csNotes.Count < 2)
                    {
                        csRoot = "C";
                        csScale = "major";
                        csOctave = 4;
                        csNotes = GetScaleNotes(csRoot + csOctave, csScale);
                    }

                    // Audio plays the root only; user must complete the rest on the staff.
                    var csMelody = new[] {
                        new { type = "note", note = csNotes[0], durationBeats = 4.0, durationLabel = "w" }
                    };
                    var csAnswer = string.Join("|", csNotes.Skip(1).Select(n => $"{n}:w"));

                    return new
                    {
                        root = csRoot,
                        scale = csScale,
                        octave = csOctave,
                        scaleNotes = csNotes,
                        promptNotes = new[] { csNotes[0] },
                        melody = csMelody,
                        answerString = csAnswer
                    };
                }

                case "CompleteChord":
                    return CompleteChordRounds.Generate(filters, random);

                case "TransposeScale":
                {
                    var tsAllNotes = new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B", "Db", "Gb" };
                    var tsScales = new[] { "major", "minor" };
                    var tsRoot = filters.TryGetValue("tsRoot", out var tsR) && tsAllNotes.Contains(tsR) ? tsR : tsAllNotes[random.Next(tsAllNotes.Length)];
                    var tsScale = filters.TryGetValue("tsScale", out var tsS) && tsScales.Contains(tsS) ? tsS : "major";
                    var tsOctave = filters.TryGetValue("tsOctave", out var tsO)
                        && int.TryParse(tsO, out var tsOctP)
                        && (tsOctP == 3 || tsOctP == 4)
                        ? tsOctP
                        : 4;

                    string tsTarget;
                    do { tsTarget = tsAllNotes[random.Next(tsAllNotes.Length)]; } while (tsTarget == tsRoot);

                    var tsOriginal = GetScaleNotes(tsRoot + tsOctave, tsScale);
                    var tsTransposed = GetScaleNotes(tsTarget + tsOctave, tsScale);
                    if (tsOriginal.Count < 2 || tsTransposed.Count < 2)
                    {
                        tsRoot = "C";
                        tsTarget = "G";
                        tsScale = "major";
                        tsOctave = 4;
                        tsOriginal = GetScaleNotes(tsRoot + tsOctave, tsScale);
                        tsTransposed = GetScaleNotes(tsTarget + tsOctave, tsScale);
                    }

                    // Audio plays the original scale; user enters the transposed scale.
                    var tsMelody = tsOriginal.Select(n => new
                    {
                        type = "note",
                        note = n,
                        durationBeats = 1.0,
                        durationLabel = "q"
                    }).ToArray();
                    var tsAnswer = string.Join("|", tsTransposed.Select(n => $"{n}:q"));

                    return new
                    {
                        originalRoot = tsRoot,
                        targetRoot = tsTarget,
                        scale = tsScale,
                        octave = tsOctave,
                        originalNotes = tsOriginal,
                        transposedNotes = tsTransposed,
                        melody = tsMelody,
                        answerString = tsAnswer
                    };
                }

                case "MelodicDictation":
                {
                    var mdAllNotes = new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B", "Db", "Gb" };
                    var mdScales = new[] { "major", "minor" };
                    var mdRoot = filters.TryGetValue("mdRoot", out var mdR) && mdAllNotes.Contains(mdR) ? mdR : mdAllNotes[random.Next(mdAllNotes.Length)];
                    var mdScale = filters.TryGetValue("mdScale", out var mdS) && mdScales.Contains(mdS) ? mdS : "major";
                    var mdOctave = filters.TryGetValue("mdOctave", out var mdO)
                        && int.TryParse(mdO, out var mdOctP)
                        && (mdOctP == 3 || mdOctP == 4)
                        ? mdOctP
                        : 4;
                    var mdLevel = filters.TryGetValue("mdLevel", out var mdL)
                        && int.TryParse(mdL, out var mdLP)
                        && new[] { 1, 3, 4 }.Contains(mdLP)
                        ? mdLP
                        : 1;
                    var mdTempo = DictationRhythm.Tempo(filters.GetValueOrDefault("mdTempo"));

                    var mdScaleNotes = GetScaleNotes(mdRoot + mdOctave, mdScale);
                    if (mdScaleNotes.Count < 3)
                    {
                        mdRoot = "C";
                        mdScale = "major";
                        mdOctave = 4;
                        mdScaleNotes = GetScaleNotes(mdRoot + mdOctave, mdScale);
                    }

                    var mdAvail = mdLevel switch
                    {
                        1 => new[] { "w", "h" },
                        3 => new[] { "w", "h", "q" },
                        _ => new[] { "w", "h", "q", "8" },
                    };
                    var mdAllowRests = mdLevel >= 3;
                    var mdRestChance = mdLevel >= 3 ? 0.15 : 0.0;
                    var mdSigPool = mdLevel <= 2 ? new[] { "4/4" } : mdLevel <= 3 ? new[] { "4/4", "3/4" } : new[] { "4/4", "3/4", "2/4", "6/8" };
                    var mdSig = mdSigPool[random.Next(mdSigPool.Length)];
                    var mdMeasureFilter = filters.TryGetValue("mdMeasures", out var mdMc) && mdMc == "long" ? "long" : "short";
                    var mdNumMeasures = mdMeasureFilter == "long" ? 4 : 2;

                    // A melody in 6/8 is built of the figures of compound meter, as RhythmDictation's
                    // level 8 is; in the other meters the bars are filled at random with the level's values.
                    string[] mdDurations;
                    string[] mdRests;
                    IReadOnlyList<IReadOnlyList<string>> mdBars;
                    if (DictationRhythm.IsCompound(mdSig))
                    {
                        mdDurations = DictationRhythm.Compound.Durations.ToArray();
                        mdRests = DictationRhythm.Compound.Rests.ToArray();
                        mdBars = DictationRhythm.Bars(DictationRhythm.Compound, mdSig, mdNumMeasures, random, melodic: true);
                    }
                    else
                    {
                        mdDurations = mdAvail;
                        mdRests = mdAllowRests ? mdAvail.Select(d => d + "r").ToArray() : Array.Empty<string>();
                        mdBars = DictationRhythm.RandomBars(mdAvail, mdRestChance, mdSig, mdNumMeasures, random, melodic: true);
                    }

                    // The melody starts on the tonic, which is given on the staff and left out of the answer.
                    var mdMelodyEntries = new List<object>();
                    var mdAnsParts = new List<string>();
                    for (var m = 0; m < mdBars.Count; m++)
                    {
                        if (m > 0) mdAnsParts.Add("bar");
                        foreach (var label in mdBars[m])
                        {
                            var isFirstEntry = mdMelodyEntries.Count == 0;
                            var isRest = DictationRhythm.IsRest(label);
                            var note = isRest ? "rest" : isFirstEntry ? mdScaleNotes[0] : mdScaleNotes[random.Next(mdScaleNotes.Count)];
                            mdMelodyEntries.Add(new { type = isRest ? "rest" : "note", note, durationBeats = DictationRhythm.Beats(label), durationLabel = label });
                            if (!isFirstEntry)
                            {
                                mdAnsParts.Add(isRest ? $"rest:{label}" : $"{note}:{label}");
                            }
                        }
                    }

                    return new
                    {
                        root = mdRoot,
                        scale = mdScale,
                        octave = mdOctave,
                        timeSignature = mdSig,
                        numMeasures = mdNumMeasures,
                        level = mdLevel,
                        tempo = mdTempo,
                        // The figures the melody may use and its rests, offered by the editor.
                        durations = mdDurations,
                        rests = mdRests.Length > 0,
                        restDurations = mdRests,
                        firstNote = mdScaleNotes[0],
                        firstDuration = mdBars[0][0],
                        melody = mdMelodyEntries,
                        answerString = string.Join("|", mdAnsParts)
                    };
                }

                case "RhythmDictation":
                {
                    var rdLevel = filters.TryGetValue("rdLevel", out var rdL)
                        && int.TryParse(rdL, out var rdLP)
                        && DictationRhythm.IsLevel(rdLP)
                        ? rdLP
                        : 1;
                    var rdTempo = DictationRhythm.Tempo(filters.GetValueOrDefault("rdTempo"));
                    var rdMeasureFilter = filters.TryGetValue("rdMeasures", out var rdMc) && rdMc == "long" ? "long" : "short";
                    var rdNumMeasures = rdMeasureFilter == "long" ? 4 : 2;

                    // From level 5 each level teaches a figure, and its bars are built of figures
                    // that start on a beat; the first levels fill their bars at random.
                    string rdSig;
                    string[] rdDurations;
                    string[] rdRests;
                    IReadOnlyList<IReadOnlyList<string>> rdBars;
                    if (DictationRhythm.Find(rdLevel) is { } rdCells)
                    {
                        rdSig = rdCells.TimeSignatures[random.Next(rdCells.TimeSignatures.Count)];
                        rdDurations = rdCells.Durations.ToArray();
                        rdRests = rdCells.Rests.ToArray();
                        rdBars = DictationRhythm.Bars(rdCells, rdSig, rdNumMeasures, random, melodic: false);
                    }
                    else
                    {
                        var rdFirst = DictationRhythm.FindRandom(rdLevel)!;
                        rdSig = rdFirst.TimeSignatures[random.Next(rdFirst.TimeSignatures.Count)];
                        rdDurations = rdFirst.Durations.ToArray();
                        rdRests = rdFirst.Rests.ToArray();
                        rdBars = DictationRhythm.RandomBars(rdDurations, rdFirst.RestChance, rdSig, rdNumMeasures, random, melodic: false);
                    }

                    return new
                    {
                        timeSignature = rdSig,
                        numMeasures = rdNumMeasures,
                        level = rdLevel,
                        tempo = rdTempo,
                        durations = rdDurations,
                        rests = rdRests.Length > 0,
                        restDurations = rdRests,
                        melody = RhythmMelody(rdBars),
                        answerString = string.Join("|bar|", rdBars.Select(bar => string.Join("|", bar)))
                    };
                }

                case "GuessRhythmPattern":
                {
                    // Four rhythms of two bars at a level of RhythmDictation; the one played is the answer.
                    var grpLevel = filters.TryGetValue("grpLevel", out var grpL)
                        && int.TryParse(grpL, out var grpLP)
                        && DictationRhythm.IsLevel(grpLP)
                        ? grpLP
                        : 1;
                    var grpRound = RhythmChoices.Draw(grpLevel, random);

                    return new
                    {
                        timeSignature = grpRound.TimeSignature,
                        numMeasures = RhythmChoices.Measures,
                        level = grpLevel,
                        tempo = DictationRhythm.Tempo(filters.GetValueOrDefault("grpTempo")),
                        options = grpRound.Options.Select(option => option.Text).ToList(),
                        melody = RhythmMelody(grpRound.Played.Values),
                        answerString = grpRound.Played.Text
                    };
                }

                case "RhythmTap":
                {
                    // A rhythm of two bars at a level of RhythmDictation, tapped back after the count-in plays again.
                    var rtLevel = filters.TryGetValue("rtLevel", out var rtL)
                        && int.TryParse(rtL, out var rtLP)
                        && DictationRhythm.IsLevel(rtLP)
                        ? rtLP
                        : 1;
                    var rtTempo = DictationRhythm.Tempo(filters.GetValueOrDefault("rtTempo"));
                    var (rtTimeSignature, rtBars) = RhythmTaps.Draw(rtLevel, random);

                    return new
                    {
                        timeSignature = rtTimeSignature,
                        numMeasures = RhythmTaps.Measures,
                        level = rtLevel,
                        tempo = rtTempo,
                        tapsFrom = RhythmTaps.TapsFrom(rtTimeSignature, rtTempo),
                        melody = RhythmMelody(rtBars),
                        answerString = string.Join("|bar|", rtBars.Select(bar => string.Join("|", bar)))
                    };
                }

                default:
                    return new { message = "Exercício sem gerador de nota implementado." };
            }
        }

        // A rhythm played on one note, as the planner plays a dictation: its note values bar by bar.
        private static List<object> RhythmMelody(IReadOnlyList<IReadOnlyList<string>> bars) =>
        [
            .. bars.SelectMany(bar => bar).Select(label => (object)new
            {
                type = DictationRhythm.IsRest(label) ? "rest" : "note",
                note = "C5",
                durationBeats = DictationRhythm.Beats(label),
                durationLabel = label
            })
        ];

        // Métodos auxiliares para IntervalMelodico
        public static string GetDegreeInScale(string note, List<string> scaleNotes)
        {
            var noteWithoutOctave = Regex.Replace(note, @"\d", "");
            for (int i = 0; i < scaleNotes.Count; i++)
            {
                var scaleNoteWithoutOctave = Regex.Replace(scaleNotes[i], @"\d", "");
                if (scaleNoteWithoutOctave == noteWithoutOctave)
                    return NumberToRomanNumeral(i + 1); // Graus começam em 1
            }
            return "I"; // Default ao primeiro grau se não encontrar
        }

        // Converte número em numeral romano (para graus musicais)
        public static string NumberToRomanNumeral(int number)
        {
            return number switch
            {
                1 => "I",
                2 => "II",
                3 => "III",
                4 => "IV",
                5 => "V",
                6 => "VI",
                7 => "VII",
                _ => "I" // Default para grau I se fora do range
            };
        }

        /// <summary>
        /// Names the interval between two notes with the codes used by the
        /// answer options ("1J", "2m", "2M", ..., "8J"). Direction is ignored
        /// and compound intervals are reduced to a simple one (a 10th is
        /// named like a 3rd). Six semitones are named "4A".
        /// </summary>
        public static string GetIntervalBetweenNotes(string note1, string note2)
        {
            var midi1 = NoteToMidi(note1) ?? 60;
            var midi2 = NoteToMidi(note2) ?? 60;
            var semitones = Math.Abs(midi2 - midi1);
            if (semitones > 12)
                semitones = semitones % 12 == 0 ? 12 : semitones % 12;

            return semitones switch
            {
                0 => "1J",
                1 => "2m",
                2 => "2M",
                3 => "3m",
                4 => "3M",
                5 => "4J",
                6 => "4A",
                7 => "5J",
                8 => "6m",
                9 => "6M",
                10 => "7m",
                11 => "7M",
                _ => "8J"
            };
        }
        #endregion

    }
}