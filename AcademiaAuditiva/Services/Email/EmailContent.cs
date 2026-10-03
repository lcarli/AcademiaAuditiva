namespace AcademiaAuditiva.Services.Email;

/// <summary>
/// The localized texts of one e-mail, as <see cref="EmailLayout"/> shows them.
/// Plain text only: the layout encodes everything it renders.
/// </summary>
/// <param name="Language">Culture of the texts, for <c>&lt;html lang&gt;</c>.</param>
/// <param name="Highlight">Shown in a box under the intro, e.g. the classroom name.</param>
/// <param name="ActionUrl">Where the button goes. It is also written out under the button.</param>
/// <param name="Notes">Small print under the button.</param>
/// <param name="Reason">Why the person got the e-mail and what to do if it wasn't meant for them.</param>
public sealed record EmailContent(
    string Language,
    string Subject,
    string Title,
    string Intro,
    string? Highlight,
    string ActionText,
    string ActionUrl,
    string LinkHint,
    IReadOnlyList<string> Notes,
    string Reason,
    string Tagline);
