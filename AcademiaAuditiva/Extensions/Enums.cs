namespace AcademiaAuditiva.Extensions
{
    public enum ExerciseType
    {
        NoteRecognition,
        ChordRecognition,
        IntervalRecognition,
        FunctionRecognition,
        MelodyReproduction,
        RhythmPatterns,
        HarmonicField,
        ScaleRecognition
    }

    public enum ExerciseCategory
    {
        Harmony,
        Melody,
        Rhythm,
        EarTraining,
        Scales,
        Games,
        Misc
    }


    public enum DifficultyLevel
    {
        Beginner,
        Intermediate,
        Advanced
    }

    public enum ChordType
    {
        Major,
        Minor,
        Diminished
        // ... outros tipos que você desejar
    }

    public enum IntervalType
    {
        Seventh,
        Ninth,
        Eleventh,
        // ... outros intervalos que você desejar
    }

    public enum FullIntervalType
    {
        MinorSecond,
        MajorSecond,
        MinorThird,
        MajorThird,
        PerfectFourth,
        Tritone,
        PerfectFifth,
        MinorSixth,
        MajorSixth,
        MinorSeventh,
        MajorSeventh,
        PerfectOctave
    }

}
