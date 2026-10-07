using AcademiaAuditiva.Services.ExerciseValidators;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Coverage for the shared StaffSequenceHelpers normaliser used by all
/// 5 staff-based exercise validators. Verifies bar/rest/accidental
/// aliases all collapse to a canonical form before comparison.
/// </summary>
public class StaffSequenceValidatorTests
{
    [Fact]
    public void CompleteScale_ExactMatch_PassesValidation()
    {
        var v = new CompleteScaleValidator();
        var json = "{\"answerString\":\"D4:w|E4:w|F4:w\"}";
        v.Validate("D4:w|E4:w|F4:w", json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void CompleteScale_CaseAndAccidentalSymbols_AreNormalised()
    {
        var v = new CompleteScaleValidator();
        var json = "{\"answerString\":\"D#4:w|E4:w|F4:w\"}";
        // User typed using musical sharp glyph + uppercase note name
        v.Validate("D♯4:W|E4:W|F4:W", json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void StaffSequence_EnharmonicFlatSpellings_AreAccepted()
    {
        var v = new CompleteScaleValidator();
        var json = "{\"answerString\":\"A#4:w|C#5:q\"}";

        v.Validate("Bb4:w|Db5:q", json).IsCorrect.Should().BeTrue();
    }

    [Theory]
    [InlineData("F5:w", "E#5:w")]
    [InlineData("C5:w", "B#4:w")]
    [InlineData("B4:w", "Cb5:w")]
    [InlineData("E4:w", "Fb4:w")]
    [InlineData("E#5:w", "F5:w")]
    [InlineData("B#4:w", "C5:w")]
    [InlineData("Cb5:w", "B4:w")]
    [InlineData("Fb4:w", "E4:w")]
    public void StaffSequence_OctaveCarryAccidentals_AreAccepted(string expected, string guess)
    {
        var v = new CompleteScaleValidator();
        var json = $$"""{"answerString":"{{expected}}"}""";

        v.Validate(guess, json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void StaffSequence_TextbookSharpMajorScales_AreAccepted()
    {
        var v = new CompleteScaleValidator();
        var fSharpMajor = "{\"answerString\":\"G#4:w|A#4:w|B4:w|C#5:w|D#5:w|E#5:w|F#5:w\"}";
        var cSharpMajor = "{\"answerString\":\"D#4:w|E#4:w|F#4:w|G#4:w|A#4:w|B#4:w|C#5:w\"}";

        v.Validate("G#4:w|A#4:w|B4:w|C#5:w|D#5:w|F5:w|F#5:w", fSharpMajor).IsCorrect.Should().BeTrue();
        v.Validate("D#4:w|F4:w|F#4:w|G#4:w|A#4:w|C5:w|C#5:w", cSharpMajor).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void StaffSequence_EnharmonicMatch_StillRequiresMatchingDuration()
    {
        var v = new CompleteScaleValidator();
        var json = "{\"answerString\":\"A#4:w\"}";

        v.Validate("Bb4:q", json).IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void StaffSequence_BarAliasesAreEquivalent()
    {
        var v = new MelodicDictationValidator();
        var json = "{\"answerString\":\"D4:q|barline|E4:q\"}";
        v.Validate("D4:q|bar|E4:q", json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void StaffSequence_RestAliasesAreEquivalent()
    {
        var v = new MelodicDictationValidator();
        // Server canonical form uses "rest:qr"; user input may carry "B4:qr".
        var json = "{\"answerString\":\"rest:qr|D4:q\"}";
        v.Validate("B4:qr|D4:q", json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void RhythmDictation_IgnoresNoteNames()
    {
        var v = new RhythmDictationValidator();
        var json = "{\"answerString\":\"q|h|qr|bar|w\"}";
        // User entered note:duration pairs — validator must strip note halves.
        v.Validate("C5:q|D4:h|B4:qr|bar|F#3:w", json).IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void Dictation_DottedValuesSixteenthsAndDottedRests_AreTheirOwnFigures()
    {
        const string rhythm = "{\"answerString\":\"q.|8|q.r|bar|8.|16|16|16|8\"}";
        const string melody = "{\"answerString\":\"D4:q.|E4:8|rest:q.r|bar|F#4:8.|G4:16\"}";

        new RhythmDictationValidator().Validate("q.|8|q.r|bar|8.|16|16|16|8", rhythm).IsCorrect.Should().BeTrue();
        new RhythmDictationValidator().Validate("q|8|q.r|bar|8.|16|16|16|8", rhythm).IsCorrect.Should().BeFalse();
        new RhythmDictationValidator().Validate("q.|8|qr|bar|8.|16|16|16|8", rhythm).IsCorrect.Should().BeFalse();
        new MelodicDictationValidator().Validate("D4:q.|E4:8|B4:q.r|bar|F#4:8.|G4:16", melody).IsCorrect.Should().BeTrue();
        new MelodicDictationValidator().Validate("D4:q.|E4:8|rest:q.r|bar|F#4:8|G4:16", melody).IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void CompleteScale_WrongOrder_FailsValidation()
    {
        var v = new CompleteScaleValidator();
        var json = "{\"answerString\":\"D4:w|E4:w|F4:w\"}";
        v.Validate("E4:w|D4:w|F4:w", json).IsCorrect.Should().BeFalse();
    }

    [Theory]
    [InlineData("C4:w|E4:w|G4:w|Bb4:w")]
    [InlineData("Bb4:w|G4:w|E4:w|C4:w")]
    [InlineData("E4:w|A#4:w|C4:w|G4:w")]
    [InlineData("B#3:w|Fb4:w|G4:w|Bb4:w")]
    public void CompleteChord_TheNotesOfTheChord_InAnyOrder_PassValidation(string guess)
    {
        // The notes of a chord are stacked on the staff: they have no order.
        var json = "{\"answerString\":\"C4:w|E4:w|G4:w|Bb4:w\"}";

        new CompleteChordValidator().Validate(guess, json).IsCorrect.Should().BeTrue();
    }

    [Theory]
    [InlineData("C4:w|E4:w|G4:w")]
    [InlineData("C4:w|E4:w|G4:w|Bb4:w|D5:w")]
    [InlineData("C4:w|E4:w|G4:w|G4:w")]
    [InlineData("C4:w|E4:w|G4:w|B4:w")]
    [InlineData("C4:w|E4:w|G4:w|Bb3:w")]
    [InlineData("C4:h|E4:w|G4:w|Bb4:w")]
    [InlineData("")]
    public void CompleteChord_AnotherChord_FailsValidation(string guess)
    {
        var json = "{\"answerString\":\"C4:w|E4:w|G4:w|Bb4:w\"}";

        var result = new CompleteChordValidator().Validate(guess, json);

        result.IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void Validators_AllRegisterCorrectExerciseName()
    {
        new CompleteScaleValidator().ExerciseName.Should().Be("CompleteScale");
        new CompleteChordValidator().ExerciseName.Should().Be("CompleteChord");
        new TransposeScaleValidator().ExerciseName.Should().Be("TransposeScale");
        new MelodicDictationValidator().ExerciseName.Should().Be("MelodicDictation");
        new RhythmDictationValidator().ExerciseName.Should().Be("RhythmDictation");
    }
}
