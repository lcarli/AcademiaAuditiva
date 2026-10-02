using System.Security.Claims;
using System.Text.Json;
using AcademiaAuditiva.Controllers;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Services.Audio;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AcademiaAuditiva.UnitTests;

public class ExploreControllerTests
{
    private const string UserId = "student-1";
    private const string Clip = "piano-audio-mixed/mix-abc.wav";

    private readonly Mock<IAudioMixerService> _mixer = new();
    private readonly Mock<IAudioTokenService> _tokens = new();

    private ExploreController CreateController(string? userId = UserId) => new(new ExploreSoundBuilder(), _mixer.Object, _tokens.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(userId is null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
            }
        }
    };

    [Fact]
    public async Task Play_MixesThePickedSound_AndReturnsATokenForIt_WithTheSpelledNotes()
    {
        IReadOnlyList<MixInput>? plan = null;
        _mixer.Setup(m => m.MixAsync(It.IsAny<IReadOnlyList<MixInput>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<MixInput> inputs, CancellationToken _) => plan = inputs)
            .ReturnsAsync(new MixedAudio("piano-audio-mixed", "mix-abc.wav"));
        _tokens.Setup(t => t.IssueTokenAsync(UserId, Clip, It.IsAny<CancellationToken>())).ReturnsAsync("token-1");

        var result = await CreateController().Play(new ExplorePlayRequest { Kind = "chord", Root = "C#", Octave = 4, Quality = "major" });

        var json = result.Should().BeOfType<JsonResult>().Subject;
        JsonSerializer.Serialize(json.Value).Should().Be("""{"token":"token-1","root":"Db","notes":["Db4","F4","Ab4"],"simultaneous":true}""");
        plan!.Select(i => i.BlobName).Should().Equal("Cs4.mp3", "F4.mp3", "Gs4.mp3");
        _tokens.Verify(t => t.IssueTokenAsync(UserId, Clip, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"kind":"chord","root":"C","octave":4,"quality":"power"}""")]
    [InlineData("""{"kind":"note","root":"C","octave":9}""")]
    public async Task Play_RefusesAnythingOffTheLists_WithoutMixing(string json)
    {
        var result = await CreateController().Play(JsonSerializer.Deserialize<ExplorePlayRequest>(json, JsonSerializerOptions.Web));

        result.Should().BeOfType<BadRequestResult>();
        _mixer.VerifyNoOtherCalls();
        _tokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Play_RefusesABodyThatCouldNotBeRead()
    {
        var controller = CreateController();
        controller.ModelState.AddModelError("$.octave", "The JSON value could not be converted.");

        var result = await controller.Play(new ExplorePlayRequest { Kind = "note", Root = "C", Octave = 4 });

        result.Should().BeOfType<BadRequestResult>();
        _mixer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Play_NeedsToKnowTheUser_BeforeMixing()
    {
        var result = await CreateController(userId: null).Play(new ExplorePlayRequest { Kind = "note", Root = "C", Octave = 4 });

        result.Should().BeOfType<UnauthorizedResult>();
        _mixer.VerifyNoOtherCalls();
        _tokens.VerifyNoOtherCalls();
    }
}
