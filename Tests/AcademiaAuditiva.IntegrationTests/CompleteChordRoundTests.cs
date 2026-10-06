using System.Net.Http.Json;
using System.Text.Json;
using AcademiaAuditiva.Data;
using AcademiaAuditiva.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// CompleteChord plays a chord for the student to write on the staff. Its round tells the
/// staff editor the clef, the root when it is given and how many notes to take, but never
/// the chord, and the notes are right in any order: they are stacked.
/// </summary>
public class CompleteChordRoundTests : IClassFixture<ExploreWebApplicationFactory>
{
    private readonly ExploreWebApplicationFactory _factory;

    public CompleteChordRoundTests(ExploreWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Mixer.Plans.Clear();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Exercises.Any()) SeedData.SeedExercises(db);
    }

    [Fact]
    public async Task Round_WithTheRootGiven_PutsItOnTheStaff_AndTakesTheOtherNotesInAnyOrder()
    {
        var (exerciseId, client) = await StartAsync();
        var filters = new Dictionary<string, string> { ["ccQuality"] = "sevenths", ["ccAccidentals"] = "any" };

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, filters }));

        var chord = PlayedChord();
        chord.Should().HaveCount(4, "a seventh chord is played");
        var metadata = play.GetProperty("metadata");
        metadata.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["promptNotes", "clef", "octave", "slots"], "the round doesn't tell the chord");
        metadata.GetProperty("promptNotes").EnumerateArray().Select(note => Midi(note.GetString()!)).Should().Equal(chord[0]);
        metadata.GetProperty("clef").GetString().Should().Be("treble");
        metadata.GetProperty("octave").GetInt32().Should().Be(4);
        metadata.GetProperty("slots").GetInt32().Should().Be(3);

        // The notes above the root, from the top down, spelled with sharps.
        var validation = await ValidateAsync(client, exerciseId, play, chord.Skip(1).Reverse());

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Round_WithTheRootHidden_LeavesTheBassStaffEmpty_ForTheWholeChord()
    {
        var (exerciseId, client) = await StartAsync();
        var filters = new Dictionary<string, string> { ["ccQuality"] = "triads", ["ccRoot"] = "hidden", ["ccOctave"] = "3" };

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId, filters }));

        var chord = PlayedChord();
        chord.Should().HaveCount(3, "a triad is played");
        chord[0].Should().BeInRange(Midi("C3"), Midi("B3"), "its root is in octave 3");
        var metadata = play.GetProperty("metadata");
        metadata.GetProperty("promptNotes").GetArrayLength().Should().Be(0);
        metadata.GetProperty("clef").GetString().Should().Be("bass");
        metadata.GetProperty("octave").GetInt32().Should().Be(3);
        metadata.GetProperty("slots").GetInt32().Should().Be(3);

        var validation = await ValidateAsync(client, exerciseId, play, [chord[1], chord[0], chord[2]]);

        validation.GetProperty("isCorrect").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Round_AnsweredWithTheNotesInTheWrongOctave_IsWrong()
    {
        var (exerciseId, client) = await StartAsync();

        var play = await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/RequestPlay", new { exerciseId }));

        var chord = PlayedChord();
        var validation = await ValidateAsync(client, exerciseId, play, chord.Skip(1).Select(midi => midi + 12));

        validation.GetProperty("success").GetBoolean().Should().BeTrue();
        validation.GetProperty("isCorrect").GetBoolean().Should().BeFalse();
    }

    private async Task<(int ExerciseId, HttpClient Client)> StartAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exerciseId = db.Exercises.Single(e => e.Name == "CompleteChord").ExerciseId;
        var client = await IntegrationHttp.WithAntiforgeryHeaderAsync(
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));
        return (exerciseId, client);
    }

    // The MIDI notes of the chord the round played, on the piano, all of them together.
    private List<int> PlayedChord()
    {
        var plan = _factory.Mixer.Plans.Should().ContainSingle().Subject;
        plan.Should().AllSatisfy(input => input.StartTimeSeconds.Should().Be(0));
        return [.. plan.Select(input => Midi(input.SampleName[..^".mp3".Length].Replace('s', '#')))];
    }

    private static async Task<JsonElement> ValidateAsync(HttpClient client, int exerciseId, JsonElement play, IEnumerable<int> notes)
    {
        var userGuess = string.Join("|", notes.Select(midi => MusicTheoryService.MidiToNote(midi) + ":w"));
        return await IntegrationHttp.ReadJsonAsync(await client.PostAsJsonAsync("/Exercise/ValidateExercise",
            new { exerciseId, roundId = play.GetProperty("roundId").GetString(), userGuess }));
    }

    private static int Midi(string note) => MusicTheoryService.NoteToMidi(note) ?? throw new ArgumentException(note);
}
