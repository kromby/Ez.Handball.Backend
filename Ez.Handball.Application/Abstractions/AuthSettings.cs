namespace Ez.Handball.Application.Abstractions;

/// <summary>URL templates the email links are built from. "{token}" is replaced with the secret.</summary>
public sealed record AuthSettings(string VerificationUrlTemplate, string ResetUrlTemplate);
