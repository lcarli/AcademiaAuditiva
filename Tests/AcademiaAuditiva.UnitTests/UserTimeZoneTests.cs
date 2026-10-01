using AcademiaAuditiva.Services.Gamification;
using Microsoft.AspNetCore.Http;

namespace AcademiaAuditiva.UnitTests;

/// <summary>The aa_tz cookie is user input: only well-formed IANA ids are looked up.</summary>
public class UserTimeZoneTests
{
    private static readonly DateTime January = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime July = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    [InlineData("../../etc/passwd")]
    [InlineData("%2e%2e%2fetc%2fpasswd")]
    [InlineData("America/Toronto<script>")]
    [InlineData("%ZZ")]
    public void Parse_FallsBackToUtc_ForMissingUnknownOrMalformedIds(string? value)
    {
        UserTimeZone.Parse(value).Should().BeSameAs(TimeZoneInfo.Utc);
    }

    [Fact]
    public void Parse_RejectsIdsLongerThan64Characters()
    {
        UserTimeZone.Parse(new string('A', 65)).Should().BeSameAs(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData("America/Toronto", -5, -4)]
    [InlineData("America%2FToronto", -5, -4)]
    [InlineData("America/Sao_Paulo", -3, -3)]
    [InlineData("Asia/Kolkata", 5.5, 5.5)]
    public void Parse_ResolvesIanaIds(string value, double januaryOffset, double julyOffset)
    {
        var timeZone = UserTimeZone.Parse(value);

        timeZone.GetUtcOffset(January).Should().Be(TimeSpan.FromHours(januaryOffset));
        timeZone.GetUtcOffset(July).Should().Be(TimeSpan.FromHours(julyOffset));
    }

    [Fact]
    public void FromRequest_ReadsTheCookie()
    {
        var withCookie = new DefaultHttpContext();
        withCookie.Request.Headers.Cookie = $"{UserTimeZone.CookieName}=America%2FToronto";

        UserTimeZone.FromRequest(withCookie.Request).GetUtcOffset(January).Should().Be(TimeSpan.FromHours(-5));
        UserTimeZone.FromRequest(new DefaultHttpContext().Request).Should().BeSameAs(TimeZoneInfo.Utc);
    }
}
