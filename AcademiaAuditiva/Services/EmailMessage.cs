namespace AcademiaAuditiva.Services;

/// <summary>
/// An e-mail ready to send: the HTML body and the same text as plain text,
/// for mail programs that don't show HTML.
/// </summary>
public sealed record EmailMessage(string Subject, string HtmlBody, string TextBody);
