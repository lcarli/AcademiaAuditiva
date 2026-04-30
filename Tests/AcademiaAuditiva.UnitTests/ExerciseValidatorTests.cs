using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services;
using AcademiaAuditiva.Services.ExerciseValidators;
using Moq;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage of the eight <see cref="IExerciseValidator"/> implementations
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
    [InlineData(typeof(GuessFullIntervalValidator), "GuessFullInterval")]
    [InlineData(typeof(GuessFunctionValidator), "GuessFunction")]
    [InlineData(typeof(GuessQualityValidator), "GuessQuality")]
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

    [Fact]
    public void IntervalMelodico_RequiresAll4Parts_AndComparesEach()
    {
        var v = new IntervalMelodicoValidator();
        var json = "{\"firstDegree\":\"1\",\"lastDegree\":\"5\",\"startInterval\":\"Unisono\",\"endInterval\":\"Quinta Justa\"}";

        v.Validate("1|5|Unisono|Quinta Justa", json).IsCorrect.Should().BeTrue();
        v.Validate("1|5|UNISONO|quinta justa", json).IsCorrect.Should().BeTrue(); // case-insensitive
        v.Validate("1|5|Unisono", json).IsCorrect.Should().BeFalse();              // arity mismatch
        v.Validate("1|5|Unisono|Terca Maior", json).IsCorrect.Should().BeFalse();  // last part wrong
        v.Validate("", json).IsCorrect.Should().BeFalse();
        v.Validate("1|5|Unisono|Quinta Justa", json).CanonicalAnswer
            .Should().Be("1|5|Unisono|Quinta Justa");
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
