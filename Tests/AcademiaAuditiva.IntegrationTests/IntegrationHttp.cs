using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcademiaAuditiva.IntegrationTests;

internal static class IntegrationHttp
{
    // The layout's language form renders the antiforgery token; the page scripts send it
    // back in this header on every same-origin POST.
    public static async Task<HttpClient> WithAntiforgeryHeaderAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Home/Privacy");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the layout renders the language form");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", match.Groups[1].Value);
        return client;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
