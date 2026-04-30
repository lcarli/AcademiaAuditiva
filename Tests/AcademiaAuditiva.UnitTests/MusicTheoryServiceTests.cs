using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services;

namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Behavioural tests for <see cref="IMusicTheoryService"/> — exercised
/// through <see cref="MusicTheoryServiceAdapter"/>, which is the production
/// implementation. Focus is on the two equivalence helpers that the
/// validators depend on; the answer parser splits on '|' so we cover each
/// arity (single note, note+quality, note+quality+inversion).
/// </summary>
public class MusicTheoryServiceTests
{
    private readonly IMusicTheoryService _svc = new MusicTheoryServiceAdapter();

    [Theory]
    [InlineData("C", "C")]
    [InlineData("C4", "C")]                  // octave digit ignored
    [InlineData("c", "C")]                   // case-insensitive
    [InlineData("C#", "Db")]                 // enharmonic sharp/flat
    [InlineData("Db", "C#")]
    [InlineData("F#", "Gb")]
    [InlineData("g#", "ab")]                 // enharmonic + casing
    [InlineData("D#5", "Eb3")]               // enharmonic + octave digits on both
    public void NotesAreEquivalent_TrueCases(string a, string b)
    {
        _svc.NotesAreEquivalent(a, b).Should().BeTrue($"{a} and {b} should be enharmonically equivalent");
    }

    [Theory]
    [InlineData("C", "D")]
    [InlineData("C#", "D#")]                 // adjacent semitones, not enharmonic
    [InlineData("C", "B")]
    public void NotesAreEquivalent_FalseCases(string a, string b)
    {
        _svc.NotesAreEquivalent(a, b).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "C")]
    [InlineData("C", null)]
    [InlineData("", "C")]
    public void NotesAreEquivalent_NullOrEmpty_DoesNotThrow(string? a, string? b)
    {
        var act = () => _svc.NotesAreEquivalent(a!, b!);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("C#", "Db")]                          // single-note enharmonic
    [InlineData("C|major", "C|major")]                // note + chord quality
    [InlineData("Db|major", "C#|major")]
    [InlineData("C|major|root", "C|major|root")]     // note + quality + inversion
    [InlineData("Db|minor|first", "C#|minor|first")]
    public void AnswersAreEquivalent_TrueCases(string user, string correct)
    {
        _svc.AnswersAreEquivalent(user, correct).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "C")]
    [InlineData("C", "")]
    [InlineData(null, "C")]
    [InlineData("C|major", "C")]                      // arity mismatch
    [InlineData("C|major", "C|minor")]                // quality mismatch
    [InlineData("C|major|root", "C|major|first")]     // inversion mismatch
    [InlineData("D|major", "C|major")]                // note mismatch
    public void AnswersAreEquivalent_FalseCases(string? user, string? correct)
    {
        _svc.AnswersAreEquivalent(user!, correct!).Should().BeFalse();
    }

    [Fact]
    public void AnswersAreEquivalent_QualityComparison_IsCaseInsensitive()
    {
        _svc.AnswersAreEquivalent("C|MAJOR", "C|major").Should().BeTrue();
        _svc.AnswersAreEquivalent("C|major|ROOT", "C|major|root").Should().BeTrue();
    }
}
