using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AcademiaAuditiva.Models;

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
            { "halfDiminished",  (new List<int>{ 3, 3 }, 3) },
            { "diminished7",     (new List<int>{ 3, 3 }, 2) },
            { "ninth",           (new List<int>{ 4, 3, 7 }, null) },
            { "diminishedMinor", (new List<int>{ 3, 3, 3 }, null) },
            { "diminishedMajor", (new List<int>{ 3, 3, 4 }, null) }
        };

        /// <summary>
        /// Dicionário contendo intervalos (em semitons) para diferentes tipos de escalas.
        /// </summary>
        private static readonly Dictionary<string, List<int>> ScaleIntervals
            = new Dictionary<string, List<int>>
        {
            { "major",           new List<int> { 2, 2, 1, 2, 2, 2, 1 } },
            { "minor",           new List<int> { 2, 1, 2, 2, 1, 2, 2 } },
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

        private const int DefaultRangeOctave = 4;

        /// <summary>
        /// Parses a <c>noteRange</c> filter such as <c>C3-C5</c> into the list of octaves it spans.
        /// The value comes from the request body/cookie, so malformed input falls back to the default
        /// octave and bounds are clamped to [<see cref="MinRangeOctave"/>, <see cref="MaxRangeOctave"/>]
        /// to keep the generated note list small.
        /// </summary>
        public static List<int> ParseOctaveRange(string? noteRange)
        {
            var parts = noteRange?.Split('-');
            if (parts is not { Length: 2 }
                || !TryParseRangeOctave(parts[0], out var start)
                || !TryParseRangeOctave(parts[1], out var end))
            {
                return new List<int> { DefaultRangeOctave };
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
        /// Gera uma melodia aleatória dentro de um certo número de compassos, com time signature e oitavas desejadas.
        /// </summary>
        /// <remarks>
        /// Inclui pausas (rests) aleatoriamente com 25% de chance.
        /// Durações possíveis: whole (4), half (2), quarter (1), eighth (0.5), sixteenth (0.25).
        /// </remarks>
        public static List<(string Note, double Duration, bool IsRest)> GenerateAdvancedMelodyWithRhythm(
            int measures = 2,
            string timeSignature = "4/4",
            List<int> octaves = null,
            bool includeRests = true)
        {
            if (octaves == null || octaves.Count == 0)
                octaves = new List<int> { 2, 3, 4, 5, 6 };

            var allNotes = GetAllNotes(octaves);
            var random = new Random();
            if (allNotes.Count == 0)
            {
                allNotes.Add(ChromaticScaleBase[random.Next(ChromaticScaleBase.Count)] + random.Next(2, 7));
            }
            var melody = new List<(string Note, double Duration, bool IsRest)>();

            var beatsPerMeasure = int.Parse(timeSignature.Split('/')[0]);
            var totalBeats = beatsPerMeasure * measures;

            // Durações possíveis com pesos: semínima, colcheia, semicolcheia, triolet, etc.
            var rhythmOptions = new List<(string Name, double Duration, double Weight)>
            {
                ("whole", 4.0, 0.5),
                ("half", 2.0, 1.0),
                ("quarter", 1.0, 2.0),
                ("eighth", 0.5, 2.5),
                ("sixteenth", 0.25, 2.5)
            };

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

                bool isRest = includeRests && random.NextDouble() < 0.25;
                string note;
                if (isRest)
                    note = "rest";
                else
                {
                    note = allNotes[random.Next(allNotes.Count)];
                    if (!System.Text.RegularExpressions.Regex.IsMatch(note, @"\d"))
                        note = note + random.Next(2, 7);
                }

                melody.Add((note, selected.Duration, isRest));
                accumulated += selected.Duration;
            }

            return melody;
        }

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

            var semitoneOffset = 0;
            var letterOffset = 0;
            result.Add(SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, semitoneOffset, letterOffset));
            foreach (var step in baseIntervals)
            {
                semitoneOffset += step;
                letterOffset += 2;
                result.Add(SpellByLetterOffset(rootMidi.Value, rootLetter, rootOctave, semitoneOffset, letterOffset));
            }

            if (seventhInterval.HasValue)
            {
                semitoneOffset += seventhInterval.Value;
                letterOffset += 2;
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
        public static object GenerateNoteForExercise(Exercise exercise, Dictionary<string, string> filters)
        {
            var random = new Random();
            filters.TryGetValue("noteRange", out var noteRange);

            switch (exercise.Name)
            {
                case "GuessNote":
                    var octaveList = ParseOctaveRange(noteRange);

                    var allNotes = GetAllNotes(octaveList);
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
                    var hlOctaves = ParseOctaveRange(noteRange);

                    if (hlOctaves.Count < 2)
                    {
                        var octave = hlOctaves[0];
                        hlOctaves.Add(octave < MaxRangeOctave ? octave + 1 : octave - 1);
                        hlOctaves.Sort();
                    }

                    var hlAllNotes = GetAllNotes(hlOctaves);
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
                    var cadChords = cadChosen.Funcs
                        .Select(fn => GetChordFromFunction(cadRoot + "3", cadScale, fn))
                        .ToList();

                    if (cadChords.Any(c => c.Count < 2))
                        return new { error = "Cadência não pôde ser gerada." };

                    return new
                    {
                        cadence = cadChosen.Name,
                        chords = cadChords
                    };

                case "GuessInversion":
                    var invOctave = filters.TryGetValue("invOctave", out var invOct) && int.TryParse(invOct, out var invOctParsed) ? invOctParsed : 4;
                    var invQualityFilter = filters.TryGetValue("invQuality", out var invQf) ? invQf : "both";
                    List<string> invQualities = invQualityFilter switch
                    {
                        "major" => new List<string> { "major" },
                        "minor" => new List<string> { "minor" },
                        _ => new List<string> { "major", "minor" }
                    };

                    var invRootNotes = GetAllNotes(new List<int> { invOctave });
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

                    var invFinalNotes = invMidis.Select(MidiToNote).Where(n => n != null).Cast<string>().ToList();

                    return new
                    {
                        inversion = chosenInversion,
                        notes = invFinalNotes
                    };

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
                        answer = degree.ToString()
                    };
                case "GuessMissingNote":
                    var melodyLength = filters.TryGetValue("melodyLength", out var rawLen) && int.TryParse(rawLen, out var len) ? len : 5;
                    var octavesMelody = new List<int> { 3, 4 };
                    var melodyRaw = GenerateAdvancedMelodyWithRhythm(measures: 2, timeSignature: "4/4", octaves: octavesMelody, includeRests: true);

                    var melody1 = melodyRaw.Select(m => new
                    {
                        type = m.IsRest ? "rest" : "note",
                        note = m.Note,
                        duration = m.Duration
                    }).ToList();

                    // Gera uma cópia com uma nota substituída por rest
                    var melody2 = melody1.Select(x => new { x.type, x.note, x.duration }).ToList();
                    var randomIndex = random.Next(melody2.Count);
                    var shouldRemove = random.NextDouble() < 0.5;
                    if (!shouldRemove)
                        melody2 = melody1;
                    else
                        melody2[randomIndex] = new { type = "rest", note = "rest", duration = melody2[randomIndex].duration };

                    return new
                    {
                        melody1,
                        melody2,
                        answer = shouldRemove ? "diff" : "same"
                    };
                case "GuessFullInterval":
                    var tonicNote = filters.TryGetValue("keySelect", out var root) ? root + "4" : "C4";
                    var direction = filters.TryGetValue("intervalDirection", out var dir) ? dir : "asc";
                    if (direction == "both")
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
                        answer = chosenInterval
                    };
                case "GuessFunction":
                    var keyRoot = filters.TryGetValue("keySelect", out var k) ? k : "C";
                    var scaleFunc = filters.TryGetValue("scaleTypeSelect", out var s) ? s : "major";

                    var majorFunctions = new List<string> { "1-major", "2-minor", "3-minor", "4-major", "5-major", "6-minor", "7-diminished" };
                    var minorFunctions = new List<string> { "1-minor", "2-diminished", "3-major", "4-minor", "5-minor", "6-major", "7-major" };

                    var functionList = scaleFunc == "minor" ? minorFunctions : majorFunctions;
                    var selectedFunction = functionList[random.Next(functionList.Count)];

                    var chordFunc = GetChordFromFunction(keyRoot + "3", scaleFunc, selectedFunction);

                    return new
                    {
                        notes = chordFunc,
                        answer = selectedFunction
                    };
                case "GuessQuality":
                    var qualityGroup = filters.TryGetValue("chordGroup", out var group) ? group : "all";

                    List<string> allowedQualities = qualityGroup switch
                    {
                        "both" => new List<string> { "major", "minor" },
                        _ => new List<string> { "major", "major7", "minor", "minor7", "diminished", "diminished7", "augmented" }
                    };

                    var rootNotesQ = GetAllNotes(new List<int> { 3, 4 });
                    var allChordsQ = GetAllChords(rootNotesQ, allowedQualities);
                    var chordQ = allChordsQ[random.Next(allChordsQ.Count)];

                    return new
                    {
                        root = Regex.Replace(chordQ.Root, @"\\d", ""),
                        type = chordQ.Type,
                        notes = chordQ.Notes,
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
                {
                    var ccAllNotes = new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
                    var ccOctave = filters.TryGetValue("ccOctave", out var ccO)
                        && int.TryParse(ccO, out var ccOctP)
                        && (ccOctP == 3 || ccOctP == 4)
                        ? ccOctP
                        : 4;
                    var ccQualityFilter = filters.TryGetValue("ccQuality", out var ccQ) ? ccQ : "both";
                    var ccQualities = ccQualityFilter switch
                    {
                        "major" => new[] { "major" },
                        "minor" => new[] { "minor" },
                        _ => new[] { "major", "minor" }
                    };
                    var ccQuality = ccQualities[random.Next(ccQualities.Length)];
                    var ccRoot = ccAllNotes[random.Next(ccAllNotes.Length)];
                    var ccChord = GetChordNotes(ccRoot + ccOctave, ccQuality);

                    if (ccChord.Count < 3)
                        return new { error = "Acorde não pôde ser gerado." };

                    var ccMelody = new[] {
                        new { type = "note", note = ccChord[0], durationBeats = 4.0, durationLabel = "w" }
                    };
                    var ccAnswer = string.Join("|", ccChord.Skip(1).Select(n => $"{n}:w"));

                    return new
                    {
                        root = ccRoot,
                        quality = ccQuality,
                        octave = ccOctave,
                        chordNotes = ccChord,
                        promptNotes = new[] { ccChord[0] },
                        melody = ccMelody,
                        answerString = ccAnswer
                    };
                }

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
                        1 => new (double V, string L)[] { (4.0, "w"), (2.0, "h") },
                        2 => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q") },
                        3 => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q") },
                        4 => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q"), (0.5, "8") },
                        _ => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q"), (0.5, "8") },
                    };
                    var mdAllowRests = mdLevel >= 3;
                    var mdRestChance = mdLevel >= 3 ? 0.15 : 0.0;
                    var mdSigPool = mdLevel <= 2 ? new[] { "4/4" } : mdLevel <= 3 ? new[] { "4/4", "3/4" } : new[] { "4/4", "3/4", "2/4", "6/8" };
                    var mdSig = mdSigPool[random.Next(mdSigPool.Length)];
                    var mdMeasureFilter = filters.TryGetValue("mdMeasures", out var mdMc) && mdMc == "long" ? "long" : "short";
                    var mdNumMeasures = mdMeasureFilter == "long" ? 4 : 2;
                    var mdBeats = mdSig switch { "3/4" => 3.0, "2/4" => 2.0, "6/8" => 3.0, _ => 4.0 };

                    var mdMelodyEntries = new List<object>();
                    var mdAnsParts = new List<string>();
                    var mdFirstNote = mdScaleNotes[0];
                    var mdFirstDuration = "q";
                    for (var m = 0; m < mdNumMeasures; m++)
                    {
                        if (m > 0) mdAnsParts.Add("bar");
                        var rem = mdBeats;
                        while (rem > 0)
                        {
                            var poss = mdAvail.Where(d => d.V <= rem).ToArray();
                            if (poss.Length == 0) break;
                            var ch = poss[random.Next(poss.Length)];
                            var isFirstEntry = mdMelodyEntries.Count == 0;
                            var isRest = !isFirstEntry && mdAllowRests && random.NextDouble() < mdRestChance;
                            var note = isFirstEntry ? mdScaleNotes[0] : isRest ? "rest" : mdScaleNotes[random.Next(mdScaleNotes.Count)];
                            var label = isRest ? ch.L + "r" : ch.L;
                            mdMelodyEntries.Add(new { type = isRest ? "rest" : "note", note, durationBeats = ch.V, durationLabel = label });
                            if (isFirstEntry)
                            {
                                mdFirstNote = note;
                                mdFirstDuration = label;
                            }
                            else
                            {
                                mdAnsParts.Add(isRest ? $"rest:{label}" : $"{note}:{label}");
                            }
                            rem -= ch.V;
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
                        firstNote = mdFirstNote,
                        firstDuration = mdFirstDuration,
                        melody = mdMelodyEntries,
                        answerString = string.Join("|", mdAnsParts)
                    };
                }

                case "RhythmDictation":
                {
                    var rdLevel = filters.TryGetValue("rdLevel", out var rdL)
                        && int.TryParse(rdL, out var rdLP)
                        && new[] { 1, 3, 4 }.Contains(rdLP)
                        ? rdLP
                        : 1;

                    var rdAvail = rdLevel switch
                    {
                        1 => new (double V, string L)[] { (4.0, "w"), (2.0, "h") },
                        2 => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q") },
                        3 => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q") },
                        4 => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q"), (0.5, "8") },
                        _ => new (double V, string L)[] { (4.0, "w"), (2.0, "h"), (1.0, "q"), (0.5, "8") },
                    };
                    var rdAllowRests = rdLevel >= 3;
                    var rdRestChance = rdLevel >= 3 ? 0.15 : 0.0;
                    var rdSigPool = rdLevel <= 2 ? new[] { "4/4" } : rdLevel <= 3 ? new[] { "4/4", "3/4" } : new[] { "4/4", "3/4", "2/4", "6/8" };
                    var rdSig = rdSigPool[random.Next(rdSigPool.Length)];
                    var rdMeasureFilter = filters.TryGetValue("rdMeasures", out var rdMc) && rdMc == "long" ? "long" : "short";
                    var rdNumMeasures = rdMeasureFilter == "long" ? 4 : 2;
                    var rdBeats = rdSig switch { "3/4" => 3.0, "2/4" => 2.0, "6/8" => 3.0, _ => 4.0 };

                    const string rdNote = "C5";
                    var rdMelodyEntries = new List<object>();
                    var rdAnsParts = new List<string>();
                    for (var m = 0; m < rdNumMeasures; m++)
                    {
                        if (m > 0) rdAnsParts.Add("bar");
                        var rem = rdBeats;
                        while (rem > 0)
                        {
                            var poss = rdAvail.Where(d => d.V <= rem).ToArray();
                            if (poss.Length == 0) break;
                            var ch = poss[random.Next(poss.Length)];
                            var isRest = rdAllowRests && random.NextDouble() < rdRestChance;
                            var label = isRest ? ch.L + "r" : ch.L;
                            rdMelodyEntries.Add(new { type = isRest ? "rest" : "note", note = rdNote, durationBeats = ch.V, durationLabel = label });
                            rdAnsParts.Add(label);
                            rem -= ch.V;
                        }
                    }

                    return new
                    {
                        timeSignature = rdSig,
                        numMeasures = rdNumMeasures,
                        level = rdLevel,
                        melody = rdMelodyEntries,
                        answerString = string.Join("|", rdAnsParts)
                    };
                }

                default:
                    return new { message = "Exercício sem gerador de nota implementado." };
            }
        }

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