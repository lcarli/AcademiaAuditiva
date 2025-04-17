using System;
using System.Collections.Generic;
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
        public static List<(string Note, double Duration, bool IsRest)> GenerateMelodyWithRhythm(
            int measures = 2,
            string timeSignature = "4/4",
            List<int> octaves = null,
            bool includeRests = true
        )
        {
            if (octaves == null || octaves.Count == 0)
                octaves = new List<int> { 3, 4, 5 };

            // Durations em "unidades" de tempo. Com timeSignature "4/4", cada compasso = 4 beats.
            var durations = new List<(string Name, double Value)>
            {
                ("whole", 4),
                ("half", 2),
                ("quarter", 1),
                ("eighth", 0.5),
                ("sixteenth", 0.25)
            };

            // Quantidade total de "beats" que a melodia deve ter, ex: 2 compassos de 4 => 8 beats.
            var totalBeats = 0.0;
            var timeSplit = timeSignature.Split('/');
            if (timeSplit.Length == 2)
            {
                if (int.TryParse(timeSplit[0], out var beatsPerMeasure))
                {
                    totalBeats = beatsPerMeasure * measures;
                }
            }

            var allNotes = GetAllNotes(octaves);
            var melody = new List<(string Note, double Duration, bool IsRest)>();

            var random = new Random();
            double accumulated = 0;

            while (accumulated < totalBeats)
            {
                var remaining = totalBeats - accumulated;
                // Pega apenas as durações <= remaining
                var validDurations = durations.Where(d => d.Value <= remaining).ToList();

                if (validDurations.Count == 0)
                    break;

                var chosen = validDurations[random.Next(validDurations.Count)];

                bool isRest = includeRests && random.NextDouble() < 0.25;
                string note = isRest
                    ? "rest"
                    : allNotes[random.Next(allNotes.Count)];

                melody.Add((note, chosen.Value, isRest));
                accumulated += chosen.Value;
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

            if (allNotes == null || allNotes.Count == 0)
            {
                allNotes = GetAllNotes(new List<int> { 2, 3, 4, 5 });
            }

            var intervals = ScaleIntervals[scaleType];
            var rootIndex = allNotes.IndexOf(rootNote);
            if (rootIndex < 0)
                return new List<string>();

            var scaleNotes = new List<string> { rootNote };
            var currentIndex = rootIndex;

            // Percorre cada intervalo e pula semitons na lista de notas.
            foreach (var step in intervals)
            {
                currentIndex += step;
                if (currentIndex >= 0 && currentIndex < allNotes.Count)
                {
                    scaleNotes.Add(allNotes[currentIndex]);
                }
                else
                {
                    // Se estourar o range de allNotes, retornamos o que conseguimos montar.
                    // Em alguns casos pode ser útil "loopar" (usando módulo), mas aqui estamos limitando ao range.
                    break;
                }
            }

            return scaleNotes;
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


            var allNotes = GetAllNotes(new List<int> { 2, 3, 4, 5 });
            var index = allNotes.IndexOf(root);
            if (index < 0)
                return result;


            result.Add(root);
            foreach (var step in baseIntervals)
            {
                index += step;
                if (index >= allNotes.Count)
                    return new List<string>();

                result.Add(allNotes[index]);
            }

            if (seventhInterval.HasValue)
            {
                index += seventhInterval.Value;
                if (index < allNotes.Count)
                {
                    result.Add(allNotes[index]);
                }
            }

            return result;
        }

        /// <summary>
        /// Converte o nome da nota (ex: "C#4") para o número MIDI correspondente.
        /// </summary>
        public static int? NoteToMidi(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return null;

            // Mapeamento das notas para semitons a partir do C.
            var noteMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "C", 0 }, { "C#", 1 }, { "Db", 1 },
                { "D", 2 }, { "D#", 3 }, { "Eb", 3 },
                { "E", 4 }, { "Fb", 4 },
                { "F", 5 }, { "F#", 6 }, { "Gb", 6 },
                { "G", 7 }, { "G#", 8 }, { "Ab", 8 },
                { "A", 9 }, { "A#", 10 }, { "Bb", 10 },
                { "B", 11 }, { "Cb", 11 }
            };

            var pitchPart = "";
            var octavePart = "";
            for (int i = 0; i < note.Length; i++)
            {
                if (char.IsDigit(note[i]))
                {
                    pitchPart = note.Substring(0, i);
                    octavePart = note.Substring(i);
                    break;
                }
            }

            if (string.IsNullOrEmpty(pitchPart) || string.IsNullOrEmpty(octavePart))
                return null;

            if (!noteMap.ContainsKey(pitchPart))
                return null;

            if (!int.TryParse(octavePart, out int octave))
                return null;

            var semitone = noteMap[pitchPart];
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
            var enharmonicMap = new Dictionary<string, string>
            {
                { "C#", "Db" }, { "Db", "C#" },
                { "D#", "Eb" }, { "Eb", "D#" },
                { "F#", "Gb" }, { "Gb", "F#" },
                { "G#", "Ab" }, { "Ab", "G#" },
                { "A#", "Bb" }, { "Bb", "A#" }
            };

            note1 = Regex.Replace(note1 ?? "", @"\d", "").ToUpper();
            note2 = Regex.Replace(note2 ?? "", @"\d", "").ToUpper();

            if (note1 == note2) return true;
            if (enharmonicMap.TryGetValue(note1, out var mapped) && mapped == note2) return true;

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
            if (!filters.TryGetValue("noteRange", out var noteRange))
                noteRange = "C4-C4";

            switch (exercise.Name)
            {
                case "GuessNote":
                    var octaveList = new List<int>();
                    if (noteRange.Contains('-'))
                    {
                        var parts = noteRange.Split('-');
                        var startOctave = int.Parse(parts[0].Substring(1));
                        var endOctave = int.Parse(parts[1].Substring(1));
                        octaveList = Enumerable.Range(startOctave, endOctave - startOctave + 1).ToList();
                    }
                    else
                    {
                        octaveList = new List<int> { 4 };
                    }

                    var allNotes = GetAllNotes(octaveList);
                    var selectedNote = allNotes[random.Next(allNotes.Count)];
                    return new { note = selectedNote };

                case "GuessChords":
                    var chordOctaves = new List<int>();
                    if (noteRange.Contains('-'))
                    {
                        var parts = noteRange.Split('-');
                        var startOctave = int.Parse(parts[0].Substring(1));
                        var endOctave = int.Parse(parts[1].Substring(1));
                        chordOctaves = Enumerable.Range(startOctave, endOctave - startOctave + 1).ToList();
                    }
                    else
                    {
                        chordOctaves = new List<int> { 4 };
                    }

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
                    var melodyRaw = GenerateMelodyWithRhythm(measures: 2, timeSignature: "4/4", octaves: octavesMelody, includeRests: true);

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

                    var intervalOptions = new Dictionary<string, int>
                    {
                        { "2m", 1 }, { "2M", 2 },
                        { "3m", 3 }, { "3M", 4 },
                        { "4J", 5 }, { "4A", 6 },
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
                default:
                    return new { message = "Exercício sem gerador de nota implementado." };
            }
        }
        #endregion

    }
}