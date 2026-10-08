using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.ExerciseValidators;
using Moq;
using Newtonsoft.Json;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage of the nine <see cref="IExerciseValidator"/> implementations
/// plus the <see cref="ExerciseValidatorRegistry"/> wiring. The validators
/// are pure functions over a JSON expected-answer payload, so each test is
/// just feeding in a representative payload and asserting the result.
/// </summary>
public class ExerciseValidatorTests
{
    private readonly IMusicTheoryService _theory = new MusicTheoryServiceAdapter();

    [Fact]
    public void GuessNote_DelegatesToMusicTheory_ForEnharmonicEquivalence()
    {
        var v = new GuessNoteValidator(_theory);
        var json = "{\"note\":\"C#\"}";

        var ok = v.Validate("Db", json);
        var miss = v.Validate("D", json);

        ok.IsCorrect.Should().BeTrue();
        ok.CanonicalAnswer.Should().Be("C#");
        miss.IsCorrect.Should().BeFalse();
        miss.CanonicalAnswer.Should().Be("C#");
    }

    [Fact]
    public void GuessNote_UsesInjectedService_NotStaticHelper()
    {
        // Confirms the strategy actually calls IMusicTheoryService — important
        // because otherwise the DI seam exists but isn't wired.
        var mock = new Mock<IMusicTheoryService>();
        mock.Setup(s => s.NotesAreEquivalent("X", "C")).Returns(true);

        var v = new GuessNoteValidator(mock.Object);
        var result = v.Validate("X", "{\"note\":\"C\"}");

        result.IsCorrect.Should().BeTrue();
        mock.Verify(s => s.NotesAreEquivalent("X", "C"), Times.Once);
    }

    [Fact]
    public void GuessChords_BuildsRootPipeQuality_AndDelegates()
    {
        var v = new GuessChordsValidator(_theory);
        var json = "{\"root\":\"Db\",\"quality\":\"major\"}";

        var ok = v.Validate("C#|major", json);
        var miss = v.Validate("C#|minor", json);

        ok.IsCorrect.Should().BeTrue();
        ok.CanonicalAnswer.Should().Be("Db|major");
        miss.IsCorrect.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(GuessIntervalValidator), "GuessInterval")]
    [InlineData(typeof(GuessMissingNoteValidator), "GuessMissingNote")]
    [InlineData(typeof(GuessFunctionValidator), "GuessFunction")]
    [InlineData(typeof(GuessDegreeValidator), "GuessDegree")]
    [InlineData(typeof(GuessMeterValidator), "GuessMeter")]
    [InlineData(typeof(GuessProgressionValidator), "GuessProgression")]
    [InlineData(typeof(GuessQualityValidator), "GuessQuality")]
    [InlineData(typeof(HigherOrLowerValidator), "HigherOrLower")]
    public void SingleFieldValidators_MatchOnAnswerField_CaseInsensitive(System.Type validatorType, string expectedName)
    {
        var v = (IExerciseValidator)Activator.CreateInstance(validatorType)!;

        v.ExerciseName.Should().Be(expectedName);

        var json = "{\"answer\":\"Major Third\"}";
        v.Validate("major third", json).IsCorrect.Should().BeTrue();
        v.Validate("Major Third", json).IsCorrect.Should().BeTrue();
        v.Validate("Minor Third", json).IsCorrect.Should().BeFalse();
        v.Validate("major third", json).CanonicalAnswer.Should().Be("Major Third");
    }

    [Theory]
    [InlineData("3M", "3M", true)]
    [InlineData(" 3M ", "3M", true)]   // stray whitespace
    [InlineData("3m", "3M", false)]    // minor vs major third: case matters
    [InlineData("2M", "2m", false)]
    [InlineData("5d", "4A", true)]     // tritone, either name
    [InlineData("4A", "5d", true)]
    [InlineData("5d", "5d", true)]
    [InlineData("4J", "4A", false)]
    [InlineData("", "", false)]        // an empty guess never matches
    public void GuessFullInterval_ComparesCaseSensitiveCodes_AndAcceptsBothTritoneNames(string guess, string expected, bool correct)
    {
        var v = new GuessFullIntervalValidator();

        var result = v.Validate(guess, $"{{\"answer\":\"{expected}\"}}");

        v.ExerciseName.Should().Be("GuessFullInterval");
        result.IsCorrect.Should().Be(correct);
        result.CanonicalAnswer.Should().Be(expected);
    }

    [Fact]
    public void GuessScaleTypeValidator_MatchesOnScaleTypeField_CaseInsensitive()
    {
        var v = new GuessScaleTypeValidator();
        v.ExerciseName.Should().Be("GuessScaleType");

        var json = "{\"scaleType\":\"majorPentatonic\",\"notes\":[\"C4\",\"D4\"]}";
        v.Validate("majorPentatonic", json).IsCorrect.Should().BeTrue();
        v.Validate("MAJORPENTATONIC", json).IsCorrect.Should().BeTrue();
        v.Validate("major", json).IsCorrect.Should().BeFalse();
        v.Validate("majorPentatonic", json).CanonicalAnswer.Should().Be("majorPentatonic");
    }

    [Fact]
    public void GuessGreekModeValidator_MatchesOnModeField_CaseInsensitive()
    {
        var v = new GuessGreekModeValidator();
        v.ExerciseName.Should().Be("GuessGreekMode");

        var json = "{\"mode\":\"dorian\",\"notes\":[\"D4\",\"E4\"]}";
        v.Validate("dorian", json).IsCorrect.Should().BeTrue();
        v.Validate("DORIAN", json).IsCorrect.Should().BeTrue();
        v.Validate("ionian", json).IsCorrect.Should().BeFalse();
        v.Validate("dorian", json).CanonicalAnswer.Should().Be("dorian");
    }

    [Fact]
    public void GuessCadenceValidator_MatchesOnCadenceField_CaseInsensitive()
    {
        var v = new GuessCadenceValidator();
        v.ExerciseName.Should().Be("GuessCadence");

        var json = "{\"cadence\":\"deceptive\",\"chords\":[[\"C3\"]]}";
        v.Validate("deceptive", json).IsCorrect.Should().BeTrue();
        v.Validate("DECEPTIVE", json).IsCorrect.Should().BeTrue();
        v.Validate("perfect", json).IsCorrect.Should().BeFalse();
        v.Validate("deceptive", json).CanonicalAnswer.Should().Be("deceptive");
    }

    [Fact]
    public void GuessInversionValidator_MatchesOnInversionField_CaseInsensitive()
    {
        var v = new GuessInversionValidator();
        v.ExerciseName.Should().Be("GuessInversion");

        var json = "{\"inversion\":\"first\",\"notes\":[\"E4\",\"G4\",\"C5\"]}";
        v.Validate("first", json).IsCorrect.Should().BeTrue();
        v.Validate("FIRST", json).IsCorrect.Should().BeTrue();
        v.Validate("root", json).IsCorrect.Should().BeFalse();
        v.Validate("first", json).CanonicalAnswer.Should().Be("first");
    }

    [Fact]
    public void GuessTopNoteValidator_MatchesOnTopNoteField_CaseInsensitive()
    {
        var v = new GuessTopNoteValidator();
        v.ExerciseName.Should().Be("GuessTopNote");

        var json = "{\"topNote\":\"topThird\",\"quality\":\"major\",\"notes\":[\"C3\",\"G3\",\"C4\",\"E4\"]}";
        v.Validate("topThird", json).IsCorrect.Should().BeTrue();
        v.Validate("TOPTHIRD", json).IsCorrect.Should().BeTrue();
        v.Validate("topFifth", json).IsCorrect.Should().BeFalse();
        v.Validate("major", json).IsCorrect.Should().BeFalse();
        v.Validate("topThird", json).CanonicalAnswer.Should().Be("topThird");
    }

    [Theory]
    [InlineData("3|up", true)]
    [InlineData("3|UP", true)]
    [InlineData("3|down", false)] // the right note, the wrong way
    [InlineData("2|up", false)]   // the wrong note
    [InlineData("3", false)]      // both parts are needed
    [InlineData("up", false)]
    [InlineData("", false)]
    public void GuessChangedNoteValidator_NeedsTheNoteAndWhereItWent(string guess, bool correct)
    {
        var v = new GuessChangedNoteValidator();
        v.ExerciseName.Should().Be("GuessChangedNote");

        var json = "{\"melody1\":[],\"melody2\":[],\"answer\":\"3|up\"}";
        var result = v.Validate(guess, json);

        result.IsCorrect.Should().Be(correct);
        result.CanonicalAnswer.Should().Be("3|up");
    }

    [Fact]
    public void IntervalMelodico_RequiresAll4Parts_AndComparesEach()
    {
        var v = new IntervalMelodicoValidator();
        var json = "{\"firstDegree\":\"I\",\"lastDegree\":\"V\",\"startInterval\":\"1J\",\"endInterval\":\"4A\",\"melody\":[\"C4\",\"C4\",\"G4\"]}";

        v.Validate("I|V|1J|4A", json).IsCorrect.Should().BeTrue();
        v.Validate("i|v|1J|5d", json).IsCorrect.Should().BeTrue();   // degrees ignore case; tritone either name
        v.Validate(" I | V |1J|4A", json).IsCorrect.Should().BeTrue(); // whitespace around parts
        v.Validate("I|V|1j|4A", json).IsCorrect.Should().BeFalse();   // interval codes are case-sensitive
        v.Validate("I|V|1J", json).IsCorrect.Should().BeFalse();      // arity mismatch
        v.Validate("I|V|1J|4A|x", json).IsCorrect.Should().BeFalse();
        v.Validate("I|IV|1J|4A", json).IsCorrect.Should().BeFalse();  // last degree wrong
        v.Validate("I|V|2m|4A", json).IsCorrect.Should().BeFalse();   // start interval wrong
        v.Validate("", json).IsCorrect.Should().BeFalse();
        v.Validate(null!, json).IsCorrect.Should().BeFalse();
        v.Validate("I|V|1J|5d", json).CanonicalAnswer.Should().Be("I|V|1J|4A");
    }

    [Fact]
    public void IntervalMelodico_AcceptsTheAnswerItGenerated()
    {
        // End-to-end with the real generator: the canonical answer must validate.
        var exercise = new Exercise { Name = "IntervalMelodico" };
        var v = new IntervalMelodicoValidator();
        for (var i = 0; i < 50; i++)
        {
            var json = JsonConvert.SerializeObject(MusicTheoryService.GenerateNoteForExercise(
                exercise, new Dictionary<string, string> { ["keySelect"] = "D", ["scaleTypeSelect"] = "both" }));
            var canonical = v.Validate("", json).CanonicalAnswer;

            v.Validate(canonical, json).IsCorrect.Should().BeTrue(json);
        }
    }

    private static string SolfegeJson(params string[] items) => JsonConvert.SerializeObject(new
    {
        melody = items.Select(n => n == "rest"
            ? new { type = "rest", note = "rest", duration = 1.0 }
            : new { type = "note", note = n, duration = 1.0 })
    });

    [Fact]
    public void SolfegeMelody_ComparesPitchClasses_InAnyOctave()
    {
        var v = new SolfegeMelodyValidator();
        var json = SolfegeJson("C4", "rest", "E4", "G4");

        v.ExerciseName.Should().Be("SolfegeMelody");
        v.Validate("C4|E4|G4", json).IsCorrect.Should().BeTrue();
        v.Validate("C3|E3|G3", json).IsCorrect.Should().BeTrue();   // sung an octave lower
        v.Validate("C5|E4|G2", json).IsCorrect.Should().BeTrue();   // octave jumps don't matter
        v.Validate("C4|E4|A4", json).IsCorrect.Should().BeFalse();  // 3-note melody: no tolerance
        v.Validate("C4|E4", json).IsCorrect.Should().BeFalse();
        v.Validate("C4|E4|G4", json).CanonicalAnswer.Should().Be("C4|E4|G4"); // rests are left out
    }

    [Fact]
    public void SolfegeMelody_TreatsEnharmonicSpellingsAsEqual()
    {
        var v = new SolfegeMelodyValidator();

        v.Validate("Db4|Eb4|F#4", SolfegeJson("C#4", "D#4", "Gb4")).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void SolfegeMelody_CollapsesRepeatedNotes_OnBothSides()
    {
        // A held note and a repeated note sound the same to the pitch tracker.
        var v = new SolfegeMelodyValidator();
        var json = SolfegeJson("D4", "D4", "F4", "A4");

        v.Validate("D4|F4|A4", json).IsCorrect.Should().BeTrue();
        v.Validate("D4|D4|D4|F4|F4|A4", json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void SolfegeMelody_ToleratesOneMistake_FromFourDistinctNotes()
    {
        var v = new SolfegeMelodyValidator();
        var json = SolfegeJson("C4", "D4", "E4", "F4");

        v.Validate("C4|D4|E4|F4", json).IsCorrect.Should().BeTrue();
        v.Validate("C4|D4|E4|G4", json).IsCorrect.Should().BeTrue();    // one wrong note
        v.Validate("C4|D4|F4", json).IsCorrect.Should().BeTrue();       // one missing
        v.Validate("C4|D4|E4|F4|G4", json).IsCorrect.Should().BeTrue(); // one extra
        v.Validate("C4|D4|G4|A4", json).IsCorrect.Should().BeFalse();   // two wrong
        v.Validate("C4|D4", json).IsCorrect.Should().BeFalse();         // two missing
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|||")]
    [InlineData("not a note")]
    [InlineData("H4|X9|C")]
    public void SolfegeMelody_RejectsGarbage_WithoutThrowing(string guess)
    {
        var v = new SolfegeMelodyValidator();

        var result = v.Validate(guess, SolfegeJson("C4", "D4", "E4", "F4"));

        result.IsCorrect.Should().BeFalse();
        result.CanonicalAnswer.Should().Be("C4|D4|E4|F4");
    }

    [Fact]
    public void SolfegeMelody_RejectsOversizedGuesses_AndEmptyMelodies()
    {
        var v = new SolfegeMelodyValidator();
        var longGuess = string.Join("|", Enumerable.Repeat("C4", 400)); // 1199 characters
        longGuess.Length.Should().BeGreaterThan(1024);

        v.Validate(longGuess, SolfegeJson("C4")).IsCorrect.Should().BeFalse();
        v.Validate("C4", SolfegeJson("rest")).IsCorrect.Should().BeFalse();
        v.Validate("C4", "{}").IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void SolfegeMelody_AcceptsTheMelodyItGenerated()
    {
        var exercise = new Exercise { Name = "SolfegeMelody" };
        var v = new SolfegeMelodyValidator();
        for (var i = 0; i < 50; i++)
        {
            var json = JsonConvert.SerializeObject(MusicTheoryService.GenerateNoteForExercise(exercise, new Dictionary<string, string>()));
            var canonical = v.Validate("", json).CanonicalAnswer;

            canonical.Should().NotBeEmpty(json);
            v.Validate(canonical, json).IsCorrect.Should().BeTrue(json);
        }
    }

    [Fact]
    public void Registry_IndexesByName_CaseInsensitive_AndReturnsNullOnMiss()
    {
        var validators = new IExerciseValidator[]
        {
            new GuessIntervalValidator(),
            new GuessQualityValidator(),
            new IntervalMelodicoValidator()
        };
        var registry = new ExerciseValidatorRegistry(validators);

        registry.Get("GuessInterval").Should().BeOfType<GuessIntervalValidator>();
        registry.Get("guessinterval").Should().BeOfType<GuessIntervalValidator>(); // case-insensitive
        registry.Get("intervalmelodico").Should().BeOfType<IntervalMelodicoValidator>();
        registry.Get("DoesNotExist").Should().BeNull();
        registry.Get("").Should().BeNull();
        registry.Get(null!).Should().BeNull();
    }

    [Fact]
    public void Registry_DeDuplicates_DuplicateExerciseNames()
    {
        // The DI container could in principle register two validators with
        // the same name (e.g. a custom override in tests). The registry
        // should keep the first one rather than throw on construction.
        var v1 = new GuessIntervalValidator();
        var v2 = new GuessIntervalValidator();
        var registry = new ExerciseValidatorRegistry(new IExerciseValidator[] { v1, v2 });

        registry.Get("GuessInterval").Should().BeSameAs(v1);
    }
}
