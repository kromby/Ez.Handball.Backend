# Real email provider + localized transactional emails — design

**Date:** 2026-09-04
**Scope:** Backend (`Ez.Handball.Backend`) only. Resolves Backend#46.

## Problem

The backend has no real email content and no real provider. `IEmailSender`
takes `(email, link, token)` with no subject/body; `ConsoleEmailSender` logs
the link for dev, `NoopEmailSender` silently drops sends in prod. Neither
knows the recipient's language (`UserEntity.Language`, `"is"` | `"en"`),
which the Web side already resolves and stores.

Widened during brainstorming to also cover emailing a mini-league invite —
today invites are link-only (generate a token, share it however you like);
there is no way to send one to a specific address.

## Approach

Extend `IEmailSender` with a `language` parameter (and drop the existing
`token` parameter, which is dead in both current implementations — the token
is already embedded in `link`). Render all email content through a single
in-code template table (`EmailTemplates`), keyed by template × language, so
`ConsoleEmailSender` and the new real sender produce identical content. Add
`AcsEmailSender` (Azure Communication Services) as the real provider —
consistent with the existing Azure-native stack (Table Storage, Blob Storage)
and requires no new vendor account. `NoopEmailSender` remains the safe
default until ACS is actually configured.

Two other options were considered and rejected: `.resx` satellite-resource
localization (built-in .NET i18n, but adds tooling this codebase doesn't use
anywhere else for just two languages) and provider-hosted templates (faster
copy edits without a deploy, but content leaves git/code review and
`ConsoleEmailSender` couldn't render the real thing for dev without
duplicating the content locally anyway).

## `IEmailSender` interface

```csharp
public interface IEmailSender
{
    Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct);
    Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct);
    Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct);
}
```

`language` is the raw `"is"`/`"en"` value already validated by
`AuthValidation.IsValidLanguage`; callers pass `UserEntity.Language` directly.
`EmailTemplates` falls back to `"en"` for anything else (defensive only —
callers should never pass an invalid value).

## Templates

New `EmailTemplates` (internal static class, `Ez.Handball.Infrastructure/Email/EmailTemplates.cs`)
holds subject + HTML + plain-text content per template × language, as string
templates with `{link}` / `{inviterName}` / `{leagueName}` placeholders —
same substitution idiom as `AuthSettings.VerificationUrlTemplate`. The brand
name (`"Olís deildin - Fantasy"`, mirroring the Web `brand.name` i18n string)
is a hardcoded const in this file.

```csharp
internal static class EmailTemplates
{
    public static (string Subject, string Html, string Text) Verification(string language, string link);
    public static (string Subject, string Html, string Text) PasswordReset(string language, string link);
    public static (string Subject, string Html, string Text) MiniLeagueInvite(
        string language, string inviterName, string leagueName, string link);
}
```

Both `AcsEmailSender` and `ConsoleEmailSender` call these and render through
the same content — dev output matches production exactly.

## Provider: Azure Communication Services

New `AcsEmailSender : IEmailSender` (`Ez.Handball.Infrastructure/Email/AcsEmailSender.cs`),
using the `Azure.Communication.Email` package. Configuration:

- `Email:ConnectionString` — the ACS resource connection string.
- `Email:FromAddress` — the verified sender address.

`AuthInfrastructureRegistration.AddAuthInfrastructure` selects the
implementation:

```
Development                          → ConsoleEmailSender
Email:ConnectionString configured    → AcsEmailSender
otherwise                            → NoopEmailSender   (unchanged safe default)
```

If `Email:ConnectionString` is set but `Email:FromAddress` is missing, throw
`InvalidOperationException` at startup — same fail-fast style as
`Jwt:SigningKey`. This lets non-dev environments stay on `NoopEmailSender`
today and switch to `AcsEmailSender` later purely via App Service
configuration, no code change.

`ConsoleEmailSender` and `NoopEmailSender` keep their existing constructors;
both now render/log through `EmailTemplates` where they log a message (`ConsoleEmailSender`
logs the rendered subject + text body; `NoopEmailSender`'s warning-only
behavior is unchanged since it never touches content).

## Threading language through existing sends

`RegisterUseCase`, `ResendVerificationUseCase`, `RequestPasswordResetUseCase`
already fetch the `UserEntity` before calling `IEmailSender` — each now passes
`user.Language`. No new repository reads.

## New: email a mini-league invite

`GenerateInviteUseCase` always mints a fresh token and deletes the league's
prior invite on every call (single active invite per league). Reusing that
behavior for "email this person" would invalidate an earlier recipient's
still-unused link the next time someone else is invited. The new use case
instead **gets the existing invite if one exists, and only generates one if
there is none** — it never regenerates a live invite.

```csharp
public abstract record SendMiniLeagueInviteEmailResult
{
    public sealed record Sent : SendMiniLeagueInviteEmailResult;
    public sealed record LeagueNotFound : SendMiniLeagueInviteEmailResult;
    public sealed record NotMember : SendMiniLeagueInviteEmailResult;
    public sealed record InvalidEmail : SendMiniLeagueInviteEmailResult;
}

public interface ISendMiniLeagueInviteEmailUseCase
{
    Task<SendMiniLeagueInviteEmailResult> ExecuteAsync(
        string userId, string leagueId, string email, CancellationToken ct);
}
```

Logic:
1. Validate `email` via `AuthValidation` (normalize + format check) →
   `InvalidEmail` if it fails.
2. Load the league; `LeagueNotFound` if absent.
3. Confirm `userId` is a member (same check as `GetInviteUseCase`) →
   `NotMember` if not.
4. `IMiniLeagueInviteRepository.GetByLeagueAsync` — if null, generate a new
   token and add it (same token/entity shape as `GenerateInviteUseCase`, but
   no delete-the-old-one step, since there is none in this branch).
5. Fetch the inviter's `UserEntity` (`IUserRepository.GetByIdAsync(userId)`)
   for `DisplayName` (→ `inviterName`) and `Language` — the recipient may not
   have an account yet, so the inviter's language is the only signal
   available and is the reasonable default (people invite people who share
   their language).
6. Build the join link via a new `AuthSettings.InviteUrlTemplate`
   (`{token}` substitution, same pattern as the other two URL templates;
   default `"http://localhost:5173/join?token={token}"`).
7. Call `IEmailSender.SendMiniLeagueInviteEmailAsync(email, inviterName, league.Name, link, user.Language, ct)`.
8. Return `Sent`.

### Endpoint

`POST /api/mini-leagues/{id}/invite/email` in `MiniLeagueInviteEndpoints`,
under the existing authorized `/api/mini-leagues` group:

```csharp
public sealed record SendInviteEmailRequest(string? Email);

group.MapPost("/{id}/invite/email", async (
    string id, SendInviteEmailRequest req, HttpContext http,
    ISendMiniLeagueInviteEmailUseCase uc, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "invalid_email" });

    var userId = http.User.UserId();
    if (string.IsNullOrEmpty(userId))
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

    var result = await uc.ExecuteAsync(userId, id, req.Email, ct);
    return result switch
    {
        SendMiniLeagueInviteEmailResult.Sent          => Results.Ok(new { sent = true }),
        SendMiniLeagueInviteEmailResult.LeagueNotFound => Results.NotFound(new { error = "league_not_found" }),
        SendMiniLeagueInviteEmailResult.NotMember      => Results.Json(new { error = "not_member" }, statusCode: StatusCodes.Status403Forbidden),
        SendMiniLeagueInviteEmailResult.InvalidEmail   => Results.BadRequest(new { error = "invalid_email" }),
        _                                               => Results.Problem()
    };
}).RequireRateLimiting("auth-sensitive");
```

`auth-sensitive` is the same policy already applied to the other
email-triggering endpoints (`AuthEndpoints`) — this endpoint also lets an
authenticated user cause outbound email to an arbitrary third-party address.

## Configuration

New keys, following the existing `Auth:*` / `Storage:*` shape:

```json
"Auth": {
  "InviteUrlTemplate": "http://localhost:5173/join?token={token}"
},
"Email": {
  "ConnectionString": "",
  "FromAddress": ""
}
```

`AuthSettings` gains `InviteUrlTemplate` (defaulted the same way as the
existing two templates in `AuthInfrastructureRegistration`). `Email:*` is
read directly in the sender-selection logic, not wrapped in a settings
record (it's only consumed in one place, unlike `AuthSettings` which several
use cases depend on).

## Error handling

Unchanged: none of the use cases catch exceptions from `IEmailSender` today,
and this design doesn't add that. A send failure (ACS error, network) still
propagates and becomes a 500 via `ErrorJsonMiddleware`, same as any other
unhandled exception. Retry/swallow behavior is out of scope for this issue.

## Testing

- `EmailTemplates`: each template renders the correct subject and both bodies
  for `is` and `en`, with every placeholder substituted (no literal `{...}`
  left in the output).
- `AcsEmailSender`: unit-testable against a fake/mocked ACS client — verifies
  the correct `from`/`to`/subject/bodies are sent.
- `ConsoleEmailSender`: existing tests extended to assert on the new
  `language` parameter and updated signatures.
- `SendMiniLeagueInviteEmailUseCase`: get-or-create doesn't clobber an
  existing invite; membership/not-found/invalid-email branches; inviter's
  language and display name are passed through.
- Endpoint tests for `POST /{id}/invite/email`, mirroring the existing
  `MiniLeagueInviteEndpointTests` style (auth required, validation, success).
- `StubEmailSender` in `AuthEndpointTests` updated to the new interface
  shape.

## Out of scope

- Retry/queueing for failed sends.
- Tracking who was emailed an invite, or per-recipient invite tokens.
- Any languages beyond `is`/`en`.
- Fixing the pre-existing "regenerating a share link invalidates the
  previous one" behavior of `GenerateInviteUseCase` itself — the new
  get-or-create use case simply avoids *introducing* that problem for the
  email path; the link-generation endpoint's behavior is unchanged.
