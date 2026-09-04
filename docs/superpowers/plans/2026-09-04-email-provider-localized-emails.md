# Real Email Provider + Localized Transactional Emails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the backend real localized (is/en) transactional email content and a real provider (Azure Communication Services), and add the ability to email a mini-league invite.

**Architecture:** `IEmailSender` gains a `language` parameter (dropping the dead `token` parameter) and a third method for mini-league invites. All content renders through one in-code template table (`EmailTemplates`) shared by `ConsoleEmailSender` (dev) and the new `AcsEmailSender` (real provider), so dev output matches production exactly. `NoopEmailSender` remains the safe default until ACS is configured. A new use case (`SendMiniLeagueInviteEmailUseCase`) and endpoint expose emailing an invite, reusing an existing invite link rather than regenerating it.

**Tech Stack:** .NET 9 (Api/Application/Infrastructure), .NET 10 (Tests), ASP.NET Core minimal APIs, Azure Table Storage, `Azure.Communication.Email` 1.1.0, xUnit + Moq.

**Spec:** [docs/superpowers/specs/2026-09-04-email-provider-localized-emails-design.md](../specs/2026-09-04-email-provider-localized-emails-design.md)

## Global Constraints

- Languages are exactly `"is"` and `"en"` (`AuthValidation.IsValidLanguage`). `EmailTemplates` falls back to `"en"` for any other value.
- `IEmailSender`'s `token` parameter is removed everywhere — it was dead in both current implementations (the token is already embedded in `link`).
- No retry/queueing for failed sends — an ACS/network failure propagates and becomes a 500 via the existing `ErrorJsonMiddleware`, same as any other unhandled exception today.
- `ConsoleEmailSender` and the new `AcsEmailSender` must render through the exact same `EmailTemplates` functions — no content duplication between them.
- Brand name in all templates: `"Olís deildin - Fantasy"` (matches the Web `brand.name` i18n string).

---

## Task 1: Email templates

**Files:**
- Create: `Ez.Handball.Infrastructure/Email/EmailTemplates.cs`
- Test: `Ez.Handball.Tests/Infrastructure/Email/EmailTemplatesTests.cs`

**Interfaces:**
- Produces: `internal static class EmailTemplates` with three static methods, each returning `(string Subject, string Html, string Text)`:
  - `Verification(string language, string link)`
  - `PasswordReset(string language, string link)`
  - `MiniLeagueInvite(string language, string inviterName, string leagueName, string link)`

- [ ] **Step 1: Write the failing tests**

Create `Ez.Handball.Tests/Infrastructure/Email/EmailTemplatesTests.cs`:

```csharp
using Ez.Handball.Infrastructure.Email;

namespace Ez.Handball.Tests.Infrastructure.Email;

public class EmailTemplatesTests
{
    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void Verification_RendersLinkInSubjectFreeBody_NoLeftoverPlaceholders(string language)
    {
        var (subject, html, text) = EmailTemplates.Verification(language, "http://localhost/verify?token=abc");

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains("http://localhost/verify?token=abc", html);
        Assert.Contains("http://localhost/verify?token=abc", text);
        Assert.DoesNotContain("{link}", subject + html + text);
    }

    [Fact]
    public void Verification_UnknownLanguage_FallsBackToEnglish()
    {
        var (subject, _, _) = EmailTemplates.Verification("fr", "http://localhost/verify?token=abc");
        var (englishSubject, _, _) = EmailTemplates.Verification("en", "http://localhost/verify?token=abc");

        Assert.Equal(englishSubject, subject);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void PasswordReset_RendersLink_NoLeftoverPlaceholders(string language)
    {
        var (subject, html, text) = EmailTemplates.PasswordReset(language, "http://localhost/reset?token=abc");

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains("http://localhost/reset?token=abc", html);
        Assert.Contains("http://localhost/reset?token=abc", text);
        Assert.DoesNotContain("{link}", subject + html + text);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("en")]
    public void MiniLeagueInvite_RendersInviterLeagueAndLink_NoLeftoverPlaceholders(string language)
    {
        var (subject, html, text) = EmailTemplates.MiniLeagueInvite(
            language, "Jón", "Office League", "http://localhost/join?token=abc");

        Assert.Contains("Jón", subject);
        Assert.Contains("Office League", subject);
        Assert.Contains("http://localhost/join?token=abc", html);
        Assert.Contains("http://localhost/join?token=abc", text);
        Assert.DoesNotContain("{link}", subject + html + text);
        Assert.DoesNotContain("{inviterName}", subject + html + text);
        Assert.DoesNotContain("{leagueName}", subject + html + text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~EmailTemplatesTests"`
Expected: FAIL to compile — `EmailTemplates` does not exist yet.

- [ ] **Step 3: Implement `EmailTemplates`**

Create `Ez.Handball.Infrastructure/Email/EmailTemplates.cs`:

```csharp
namespace Ez.Handball.Infrastructure.Email;

internal static class EmailTemplates
{
    private const string Brand = "Olís deildin - Fantasy";

    public static (string Subject, string Html, string Text) Verification(string language, string link)
        => language == "is"
            ? (
                $"Staðfestu netfangið þitt hjá {Brand}",
                $"<p>Til að staðfesta netfangið þitt, smelltu á hlekkinn hér að neðan:</p>" +
                $"<p><a href=\"{link}\">Staðfesta netfang</a></p>" +
                $"<p>Ef hlekkurinn virkar ekki, afritaðu þessa slóð í vafrann þinn:<br>{link}</p>",
                $"Til að staðfesta netfangið þitt, farðu á eftirfarandi hlekk:\n{link}"
              )
            : (
                $"Verify your email for {Brand}",
                $"<p>To verify your email address, click the link below:</p>" +
                $"<p><a href=\"{link}\">Verify email</a></p>" +
                $"<p>If the link doesn't work, copy this URL into your browser:<br>{link}</p>",
                $"To verify your email address, follow this link:\n{link}"
              );

    public static (string Subject, string Html, string Text) PasswordReset(string language, string link)
        => language == "is"
            ? (
                $"Endurstilltu lykilorðið þitt hjá {Brand}",
                $"<p>Þú baðst um að endurstilla lykilorðið þitt. Smelltu á hlekkinn hér að neðan til að velja nýtt lykilorð:</p>" +
                $"<p><a href=\"{link}\">Endurstilla lykilorð</a></p>" +
                $"<p>Ef þú baðst ekki um þetta geturðu hunsað þennan póst.</p>",
                $"Þú baðst um að endurstilla lykilorðið þitt. Farðu á eftirfarandi hlekk til að velja nýtt lykilorð:\n{link}\n\n" +
                $"Ef þú baðst ekki um þetta geturðu hunsað þennan póst."
              )
            : (
                $"Reset your password for {Brand}",
                $"<p>You asked to reset your password. Click the link below to choose a new one:</p>" +
                $"<p><a href=\"{link}\">Reset password</a></p>" +
                $"<p>If you didn't request this, you can ignore this email.</p>",
                $"You asked to reset your password. Follow this link to choose a new one:\n{link}\n\n" +
                $"If you didn't request this, you can ignore this email."
              );

    public static (string Subject, string Html, string Text) MiniLeagueInvite(
        string language, string inviterName, string leagueName, string link)
        => language == "is"
            ? (
                $"{inviterName} bauð þér í deildina \"{leagueName}\" hjá {Brand}",
                $"<p>{inviterName} bauð þér að ganga í deildina <strong>{leagueName}</strong> hjá {Brand}.</p>" +
                $"<p><a href=\"{link}\">Skrá mig í deildina</a></p>" +
                $"<p>Ef hlekkurinn virkar ekki, afritaðu þessa slóð í vafrann þinn:<br>{link}</p>",
                $"{inviterName} bauð þér að ganga í deildina \"{leagueName}\" hjá {Brand}.\n\nGakktu í deildina hér:\n{link}"
              )
            : (
                $"{inviterName} invited you to \"{leagueName}\" on {Brand}",
                $"<p>{inviterName} invited you to join <strong>{leagueName}</strong> on {Brand}.</p>" +
                $"<p><a href=\"{link}\">Join the league</a></p>" +
                $"<p>If the link doesn't work, copy this URL into your browser:<br>{link}</p>",
                $"{inviterName} invited you to join \"{leagueName}\" on {Brand}.\n\nJoin here:\n{link}"
              );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~EmailTemplatesTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add Ez.Handball.Infrastructure/Email/EmailTemplates.cs Ez.Handball.Tests/Infrastructure/Email/EmailTemplatesTests.cs
git commit -m "feat(email): add is/en templates for verification, reset, and invite emails"
```

---

## Task 2: `IEmailSender` — add language, drop dead token param, thread language through existing sends

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/IEmailSender.cs`
- Modify: `Ez.Handball.Infrastructure/Email/ConsoleEmailSender.cs`
- Modify: `Ez.Handball.Infrastructure/Email/NoopEmailSender.cs`
- Modify: `Ez.Handball.Application/UseCases/RegisterUseCase.cs:111`
- Modify: `Ez.Handball.Application/UseCases/ResendVerificationUseCase.cs:44`
- Modify: `Ez.Handball.Application/UseCases/RequestPasswordResetUseCase.cs:45`
- Test: `Ez.Handball.Tests/Infrastructure/Email/ConsoleEmailSenderTests.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/RegisterUseCaseTests.cs:68-69`
- Test: `Ez.Handball.Tests/Application/UseCases/ResendVerificationUseCaseTests.cs:34-35`
- Test: `Ez.Handball.Tests/Application/UseCases/RequestPasswordResetUseCaseTests.cs:34-35`
- Test: `Ez.Handball.Tests/Api/Endpoints/AuthEndpointTests.cs:19-35`

**Interfaces:**
- Consumes: `EmailTemplates.Verification`, `EmailTemplates.PasswordReset`, `EmailTemplates.MiniLeagueInvite` (Task 1).
- Produces:
  ```csharp
  public interface IEmailSender
  {
      Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct);
      Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct);
      Task SendMiniLeagueInviteEmailAsync(
          string email, string inviterName, string leagueName, string link, string language, CancellationToken ct);
  }
  ```
  Every later task's `IEmailSender` consumer/implementer uses this exact shape.

- [ ] **Step 1: Update the interface**

Replace the full contents of `Ez.Handball.Application/Abstractions/IEmailSender.cs`:

```csharp
namespace Ez.Handball.Application.Abstractions;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct);
    Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct);
    Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct);
}
```

- [ ] **Step 2: Fix `ConsoleEmailSender` and `NoopEmailSender` to compile against the new interface**

Replace the full contents of `Ez.Handball.Infrastructure/Email/ConsoleEmailSender.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.Email;

internal sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, _, text) = EmailTemplates.Verification(language, link);
        Log(email, subject, text);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, _, text) = EmailTemplates.PasswordReset(language, link);
        Log(email, subject, text);
        return Task.CompletedTask;
    }

    public Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct)
    {
        var (subject, _, text) = EmailTemplates.MiniLeagueInvite(language, inviterName, leagueName, link);
        Log(email, subject, text);
        return Task.CompletedTask;
    }

    private void Log(string email, string subject, string text)
        => _logger.LogInformation("[DEV EMAIL] To: {Email}\nSubject: {Subject}\n{Body}", email, subject, text);
}
```

Replace the full contents of `Ez.Handball.Infrastructure/Email/NoopEmailSender.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Infrastructure.Email;

// Default sender outside Development: never logs the token-bearing link. A real provider
// (Azure Communication Services) is AcsEmailSender; until it's configured, non-dev
// environments get a safe no-op rather than leaking secrets to logs.
internal sealed class NoopEmailSender : IEmailSender
{
    private readonly ILogger<NoopEmailSender> _logger;

    public NoopEmailSender(ILogger<NoopEmailSender> logger) => _logger = logger;

    public Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        _logger.LogWarning("Email sending is not configured; verification email for {Email} was not sent.", email);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        _logger.LogWarning("Email sending is not configured; password-reset email for {Email} was not sent.", email);
        return Task.CompletedTask;
    }

    public Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct)
    {
        _logger.LogWarning("Email sending is not configured; mini-league invite email for {Email} was not sent.", email);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Thread `user.Language` through the three existing call sites**

In `Ez.Handball.Application/UseCases/RegisterUseCase.cs`, change line 111 from:

```csharp
        await _email.SendVerificationEmailAsync(email, link, emailToken.Value, ct);
```

to:

```csharp
        await _email.SendVerificationEmailAsync(email, link, user.Language, ct);
```

In `Ez.Handball.Application/UseCases/ResendVerificationUseCase.cs`, change line 44 from:

```csharp
        await _email.SendVerificationEmailAsync(user.Email, link, token.Value, ct);
```

to:

```csharp
        await _email.SendVerificationEmailAsync(user.Email, link, user.Language, ct);
```

In `Ez.Handball.Application/UseCases/RequestPasswordResetUseCase.cs`, change line 45 from:

```csharp
            await _email.SendPasswordResetEmailAsync(user.Email, link, token.Value, ct);
```

to:

```csharp
            await _email.SendPasswordResetEmailAsync(user.Email, link, user.Language, ct);
```

- [ ] **Step 4: Update existing tests to compile and assert on the new signature**

Replace the full contents of `Ez.Handball.Tests/Infrastructure/Email/ConsoleEmailSenderTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ez.Handball.Tests.Infrastructure.Email;

public class ConsoleEmailSenderTests
{
    private readonly IEmailSender _sut = new ConsoleEmailSender(NullLogger<ConsoleEmailSender>.Instance);

    [Fact]
    public async Task SendVerificationEmailAsync_DoesNotThrow()
        => await _sut.SendVerificationEmailAsync("a@b.is", "http://localhost/verify?token=abc", "is", default);

    [Fact]
    public async Task SendPasswordResetEmailAsync_DoesNotThrow()
        => await _sut.SendPasswordResetEmailAsync("a@b.is", "http://localhost/reset?token=abc", "en", default);

    [Fact]
    public async Task SendMiniLeagueInviteEmailAsync_DoesNotThrow()
        => await _sut.SendMiniLeagueInviteEmailAsync(
            "a@b.is", "Jón", "Office League", "http://localhost/join?token=abc", "is", default);
}
```

In `Ez.Handball.Tests/Application/UseCases/RegisterUseCaseTests.cs`, change lines 68-69 from:

```csharp
        _email.Verify(e => e.SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=evalue", "evalue", It.IsAny<CancellationToken>()), Times.Once);
```

to:

```csharp
        _email.Verify(e => e.SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=evalue", "is", It.IsAny<CancellationToken>()), Times.Once);
```

In `Ez.Handball.Tests/Application/UseCases/ResendVerificationUseCaseTests.cs`, change lines 34-35 from:

```csharp
        _email.Verify(e => e.SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=vvalue", "vvalue", It.IsAny<CancellationToken>()), Times.Once);
```

to:

```csharp
        _email.Verify(e => e.SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=vvalue", "is", It.IsAny<CancellationToken>()), Times.Once);
```

In `Ez.Handball.Tests/Application/UseCases/RequestPasswordResetUseCaseTests.cs`, change lines 34-35 from:

```csharp
        _email.Verify(e => e.SendPasswordResetEmailAsync(
            "a@b.is", "http://localhost/reset?token=rvalue", "rvalue", It.IsAny<CancellationToken>()), Times.Once);
```

to:

```csharp
        _email.Verify(e => e.SendPasswordResetEmailAsync(
            "a@b.is", "http://localhost/reset?token=rvalue", "is", It.IsAny<CancellationToken>()), Times.Once);
```

In `Ez.Handball.Tests/Api/Endpoints/AuthEndpointTests.cs`, replace lines 19-35 (the `StubEmailSender` class) with:

```csharp
    public sealed class StubEmailSender : IEmailSender
    {
        public string? LastVerificationToken;
        public string? LastResetToken;

        public Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct)
        {
            LastVerificationToken = ExtractToken(link);
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct)
        {
            LastResetToken = ExtractToken(link);
            return Task.CompletedTask;
        }

        public Task SendMiniLeagueInviteEmailAsync(
            string email, string inviterName, string leagueName, string link, string language, CancellationToken ct)
            => Task.CompletedTask;

        // The interface no longer carries the raw token (it's already embedded in `link`) — these
        // integration tests still need the raw value to drive the follow-up /verify and /reset calls.
        private static string ExtractToken(string link)
        {
            var idx = link.IndexOf("token=", StringComparison.Ordinal);
            return idx < 0 ? string.Empty : link[(idx + "token=".Length)..];
        }
    }
```

- [ ] **Step 5: Run the full test suite to verify it compiles and passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (same count as the pre-existing baseline — 978 passing, the 2 pre-existing `DebugReplayEndpointTests` failures are unrelated and unaffected).

- [ ] **Step 6: Commit**

```bash
git add Ez.Handball.Application/Abstractions/IEmailSender.cs \
        Ez.Handball.Infrastructure/Email/ConsoleEmailSender.cs \
        Ez.Handball.Infrastructure/Email/NoopEmailSender.cs \
        Ez.Handball.Application/UseCases/RegisterUseCase.cs \
        Ez.Handball.Application/UseCases/ResendVerificationUseCase.cs \
        Ez.Handball.Application/UseCases/RequestPasswordResetUseCase.cs \
        Ez.Handball.Tests/Infrastructure/Email/ConsoleEmailSenderTests.cs \
        Ez.Handball.Tests/Application/UseCases/RegisterUseCaseTests.cs \
        Ez.Handball.Tests/Application/UseCases/ResendVerificationUseCaseTests.cs \
        Ez.Handball.Tests/Application/UseCases/RequestPasswordResetUseCaseTests.cs \
        Ez.Handball.Tests/Api/Endpoints/AuthEndpointTests.cs
git commit -m "feat(email): thread user language through IEmailSender, drop dead token param"
```

---

## Task 3: Azure Communication Services provider

**Files:**
- Modify: `Ez.Handball.Infrastructure/Ez.Handball.Infrastructure.csproj`
- Create: `Ez.Handball.Infrastructure/Email/AcsEmailSender.cs`
- Modify: `Ez.Handball.Infrastructure/AuthInfrastructureRegistration.cs`
- Modify: `Ez.Handball.Api/appsettings.json`
- Test: `Ez.Handball.Tests/Infrastructure/Email/AcsEmailSenderTests.cs`

**Interfaces:**
- Consumes: `EmailTemplates.*` (Task 1), `IEmailSender` (Task 2).
- Produces: `internal sealed class AcsEmailSender(EmailClient client, string fromAddress) : IEmailSender` — later tasks don't depend on this directly (it's selected by config, not referenced by name elsewhere).

- [ ] **Step 1: Add the ACS package reference**

In `Ez.Handball.Infrastructure/Ez.Handball.Infrastructure.csproj`, add to the existing `PackageReference` `ItemGroup` (after the `Azure.Storage.Blobs` line):

```xml
    <PackageReference Include="Azure.Communication.Email" Version="1.1.0" />
```

- [ ] **Step 2: Write the failing tests**

Create `Ez.Handball.Tests/Infrastructure/Email/AcsEmailSenderTests.cs`:

```csharp
using Azure;
using Azure.Communication.Email;
using Ez.Handball.Infrastructure.Email;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Email;

public class AcsEmailSenderTests
{
    private readonly Mock<EmailClient> _client = new();

    private AcsEmailSender CreateSut()
    {
        _client.Setup(c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new EmailSendOperation("op-1", _client.Object));
        return new AcsEmailSender(_client.Object, "no-reply@handbolti.is");
    }

    [Fact]
    public async Task SendVerificationEmailAsync_SendsRenderedTemplate_ToCorrectRecipient()
    {
        await CreateSut().SendVerificationEmailAsync(
            "a@b.is", "http://localhost/verify?token=abc", "is", CancellationToken.None);

        _client.Verify(c => c.SendAsync(
            WaitUntil.Started,
            It.Is<EmailMessage>(m =>
                m.SenderAddress == "no-reply@handbolti.is" &&
                m.Recipients.To.Single().Address == "a@b.is" &&
                m.Content.Subject == "Staðfestu netfangið þitt hjá Olís deildin - Fantasy" &&
                m.Content.PlainText!.Contains("http://localhost/verify?token=abc")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_SendsRenderedTemplate_InRequestedLanguage()
    {
        await CreateSut().SendPasswordResetEmailAsync(
            "a@b.is", "http://localhost/reset?token=abc", "en", CancellationToken.None);

        _client.Verify(c => c.SendAsync(
            WaitUntil.Started,
            It.Is<EmailMessage>(m => m.Content.Subject == "Reset your password for Olís deildin - Fantasy"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMiniLeagueInviteEmailAsync_IncludesInviterAndLeagueName()
    {
        await CreateSut().SendMiniLeagueInviteEmailAsync(
            "a@b.is", "Jón", "Office League", "http://localhost/join?token=abc", "en", CancellationToken.None);

        _client.Verify(c => c.SendAsync(
            WaitUntil.Started,
            It.Is<EmailMessage>(m =>
                m.Content.Subject == "Jón invited you to \"Office League\" on Olís deildin - Fantasy"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~AcsEmailSenderTests"`
Expected: FAIL to compile — `AcsEmailSender` does not exist yet.

- [ ] **Step 4: Implement `AcsEmailSender`**

Create `Ez.Handball.Infrastructure/Email/AcsEmailSender.cs`:

```csharp
using Azure;
using Azure.Communication.Email;
using Ez.Handball.Application.Abstractions;

namespace Ez.Handball.Infrastructure.Email;

internal sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _fromAddress;

    public AcsEmailSender(EmailClient client, string fromAddress)
    {
        _client = client;
        _fromAddress = fromAddress;
    }

    public Task SendVerificationEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, html, text) = EmailTemplates.Verification(language, link);
        return SendAsync(email, subject, html, text, ct);
    }

    public Task SendPasswordResetEmailAsync(string email, string link, string language, CancellationToken ct)
    {
        var (subject, html, text) = EmailTemplates.PasswordReset(language, link);
        return SendAsync(email, subject, html, text, ct);
    }

    public Task SendMiniLeagueInviteEmailAsync(
        string email, string inviterName, string leagueName, string link, string language, CancellationToken ct)
    {
        var (subject, html, text) = EmailTemplates.MiniLeagueInvite(language, inviterName, leagueName, link);
        return SendAsync(email, subject, html, text, ct);
    }

    // WaitUntil.Started: queue the send with ACS and return immediately rather than polling for
    // final delivery status — transactional callers shouldn't block the HTTP response on ACS's
    // full send pipeline.
    private async Task SendAsync(string toAddress, string subject, string html, string text, CancellationToken ct)
    {
        var content = new EmailContent(subject) { Html = html, PlainText = text };
        var message = new EmailMessage(_fromAddress, toAddress, content);
        await _client.SendAsync(WaitUntil.Started, message, ct);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~AcsEmailSenderTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Wire provider selection into DI**

In `Ez.Handball.Infrastructure/AuthInfrastructureRegistration.cs`, add `using Azure.Communication.Email;` to the usings, then replace:

```csharp
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        if (isDevelopment)
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        else
            services.AddSingleton<IEmailSender, NoopEmailSender>();

        return services;
```

with:

```csharp
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // Development always renders through the console (safe to see real token-bearing links
        // locally); otherwise use the real ACS provider once Email:ConnectionString is configured
        // (e.g. via Azure App Service application settings), and fall back to a safe no-op until then.
        var emailConnectionString = config["Email:ConnectionString"];
        if (isDevelopment)
        {
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        }
        else if (!string.IsNullOrWhiteSpace(emailConnectionString))
        {
            var fromAddress = config["Email:FromAddress"]
                ?? throw new InvalidOperationException("Email:FromAddress is required when Email:ConnectionString is set");
            services.AddSingleton(new EmailClient(emailConnectionString));
            services.AddSingleton<IEmailSender>(sp => new AcsEmailSender(sp.GetRequiredService<EmailClient>(), fromAddress));
        }
        else
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();
        }

        return services;
```

- [ ] **Step 7: Add the `Email` config placeholder**

In `Ez.Handball.Api/appsettings.json`, add an `Email` section (after `Cors`, before `Debug`):

```json
  "Email": {
    "ConnectionString": "",
    "FromAddress": ""
  },
```

The full file should read:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Storage": {
    "ConnectionString": ""
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "Email": {
    "ConnectionString": "",
    "FromAddress": ""
  },
  "Debug": {
    "GameClock": {
      "OverrideEnabled": false
    }
  }
}
```

- [ ] **Step 8: Run the full test suite and confirm the API still builds**

Run: `dotnet build Ez.Handball.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (same as Task 2's baseline plus the 3 new `AcsEmailSenderTests`).

- [ ] **Step 9: Commit**

```bash
git add Ez.Handball.Infrastructure/Ez.Handball.Infrastructure.csproj \
        Ez.Handball.Infrastructure/Email/AcsEmailSender.cs \
        Ez.Handball.Infrastructure/AuthInfrastructureRegistration.cs \
        Ez.Handball.Api/appsettings.json \
        Ez.Handball.Tests/Infrastructure/Email/AcsEmailSenderTests.cs
git commit -m "feat(email): add Azure Communication Services provider, selected via Email:ConnectionString"
```

---

## Task 4: Email a mini-league invite (use case)

**Files:**
- Modify: `Ez.Handball.Application/Abstractions/AuthSettings.cs`
- Modify: `Ez.Handball.Infrastructure/AuthInfrastructureRegistration.cs`
- Modify: `Ez.Handball.Api/appsettings.Development.json`
- Modify: `Ez.Handball.Tests/Application/UseCases/RegisterUseCaseTests.cs:22`
- Modify: `Ez.Handball.Tests/Application/UseCases/ResendVerificationUseCaseTests.cs:16`
- Modify: `Ez.Handball.Tests/Application/UseCases/RequestPasswordResetUseCaseTests.cs:16`
- Create: `Ez.Handball.Application/UseCases/SendMiniLeagueInviteEmailUseCase.cs`
- Test: `Ez.Handball.Tests/Application/UseCases/SendMiniLeagueInviteEmailUseCaseTests.cs`

**Interfaces:**
- Consumes: `IEmailSender.SendMiniLeagueInviteEmailAsync` (Task 2); `IMiniLeagueRepository.GetAsync`/`GetMembersAsync`, `IMiniLeagueInviteRepository.GetByLeagueAsync`/`AddAsync`, `IUserRepository.GetByIdAsync`, `ITokenService.CreateInviteCode()`, `AuthValidation.NormalizeEmail`/`IsValidEmail` (all pre-existing).
- Produces:
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
  Task 5's endpoint depends on this exact interface and result shape.

- [ ] **Step 1: Add `InviteUrlTemplate` to `AuthSettings`**

Replace the full contents of `Ez.Handball.Application/Abstractions/AuthSettings.cs`:

```csharp
namespace Ez.Handball.Application.Abstractions;

/// <summary>URL templates the email links are built from. "{token}" is replaced with the secret.</summary>
public sealed record AuthSettings(string VerificationUrlTemplate, string ResetUrlTemplate, string InviteUrlTemplate);
```

- [ ] **Step 2: Fix the three existing call sites this ripples into**

In `Ez.Handball.Tests/Application/UseCases/RegisterUseCaseTests.cs`, change line 22 from:

```csharp
    private readonly AuthSettings _settings = new("http://localhost/verify?token={token}", "http://localhost/reset?token={token}");
```

to:

```csharp
    private readonly AuthSettings _settings = new(
        "http://localhost/verify?token={token}", "http://localhost/reset?token={token}", "http://localhost/join?token={token}");
```

Apply the identical change to line 16 of `Ez.Handball.Tests/Application/UseCases/ResendVerificationUseCaseTests.cs` and line 16 of `Ez.Handball.Tests/Application/UseCases/RequestPasswordResetUseCaseTests.cs`.

In `Ez.Handball.Infrastructure/AuthInfrastructureRegistration.cs`, change:

```csharp
        services.AddSingleton(new AuthSettings(
            config["Auth:VerificationUrlTemplate"] ?? "http://localhost/verify?token={token}",
            config["Auth:ResetUrlTemplate"] ?? "http://localhost/reset?token={token}"));
```

to:

```csharp
        services.AddSingleton(new AuthSettings(
            config["Auth:VerificationUrlTemplate"] ?? "http://localhost/verify?token={token}",
            config["Auth:ResetUrlTemplate"] ?? "http://localhost/reset?token={token}",
            config["Auth:InviteUrlTemplate"] ?? "http://localhost/join?token={token}"));
```

- [ ] **Step 3: Run the full test suite to verify it still compiles and passes**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (same as Task 3's count — this step is a pure ripple fix, no new tests yet).

- [ ] **Step 4: Write the failing tests for the new use case**

Create `Ez.Handball.Tests/Application/UseCases/SendMiniLeagueInviteEmailUseCaseTests.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.UseCases;
using Ez.Handball.Domain;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Application.UseCases;

public class SendMiniLeagueInviteEmailUseCaseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(100);

    private readonly Mock<IMiniLeagueRepository> _leagues = new();
    private readonly Mock<IMiniLeagueInviteRepository> _invites = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly AuthSettings _settings = new(
        "http://localhost/verify?token={token}", "http://localhost/reset?token={token}", "http://localhost/join?token={token}");

    private SendMiniLeagueInviteEmailUseCase CreateSut() =>
        new(_leagues.Object, _invites.Object, _users.Object, _tokens.Object, _email.Object, _settings, () => Now);

    private static MiniLeague League(string id = "lg-1") => new(id, "Office League", "2025-26", "u-1", Now);

    private void LeagueExists(string id = "lg-1") =>
        _leagues.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(League(id));

    private void Members(string leagueId, params string[] userIds) =>
        _leagues.Setup(r => r.GetMembersAsync(leagueId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(userIds.Select(u => new MiniLeagueMember(u, MiniLeagueRoles.Member, Now)).ToList());

    [Fact]
    public async Task InvalidEmail_ReturnsInvalidEmail_AndDoesNotSend()
    {
        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "not-an-email", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.InvalidEmail>(result);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingLeague_ReturnsLeagueNotFound()
    {
        _leagues.Setup(r => r.GetAsync("lg-x", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeague?)null);

        var result = await CreateSut().ExecuteAsync("u-1", "lg-x", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.LeagueNotFound>(result);
    }

    [Fact]
    public async Task CallerNotMember_ReturnsNotMember()
    {
        LeagueExists();
        Members("lg-1", "someone-else");

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.NotMember>(result);
    }

    [Fact]
    public async Task NoExistingInvite_GeneratesOne_AndSendsEmail()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>())).ReturnsAsync((MiniLeagueInvite?)null);
        _tokens.Setup(t => t.CreateInviteCode()).Returns("tok-new");
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "en" });

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "Friend@Example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.Sent>(result);
        _invites.Verify(r => r.AddAsync(
            It.Is<MiniLeagueInvite>(i => i.Token == "tok-new" && i.LeagueId == "lg-1" && i.CreatedByUserId == "u-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            "friend@example.com", "Jón", "Office League", "http://localhost/join?token=tok-new", "en",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingInvite_ReusesToken_DoesNotRegenerate()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-existing", "lg-1", "u-1", Now, null));
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "is" });

        var result = await CreateSut().ExecuteAsync("u-1", "lg-1", "friend@example.com", CancellationToken.None);

        Assert.IsType<SendMiniLeagueInviteEmailResult.Sent>(result);
        _invites.Verify(r => r.AddAsync(It.IsAny<MiniLeagueInvite>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokens.Verify(t => t.CreateInviteCode(), Times.Never);
        _email.Verify(e => e.SendMiniLeagueInviteEmailAsync(
            "friend@example.com", "Jón", "Office League", "http://localhost/join?token=tok-existing", "is",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SecondInviteEmail_DoesNotInvalidateFirstRecipientsLink()
    {
        LeagueExists();
        Members("lg-1", "u-1");
        _invites.Setup(r => r.GetByLeagueAsync("lg-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MiniLeagueInvite("tok-existing", "lg-1", "u-1", Now, null));
        _users.Setup(u => u.GetByIdAsync("u-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new UserEntity { RowKey = "u-1", DisplayName = "Jón", Language = "is" });

        await CreateSut().ExecuteAsync("u-1", "lg-1", "second-friend@example.com", CancellationToken.None);

        _invites.Verify(r => r.DeleteByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SendMiniLeagueInviteEmailUseCaseTests"`
Expected: FAIL to compile — `SendMiniLeagueInviteEmailUseCase` does not exist yet.

- [ ] **Step 6: Implement the use case**

Create `Ez.Handball.Application/UseCases/SendMiniLeagueInviteEmailUseCase.cs`:

```csharp
using Ez.Handball.Application.Abstractions;
using Ez.Handball.Application.Validation;
using Ez.Handball.Domain;

namespace Ez.Handball.Application.UseCases;

public abstract record SendMiniLeagueInviteEmailResult
{
    public sealed record Sent : SendMiniLeagueInviteEmailResult;
    public sealed record LeagueNotFound : SendMiniLeagueInviteEmailResult { public static readonly LeagueNotFound Instance = new(); }
    public sealed record NotMember : SendMiniLeagueInviteEmailResult { public static readonly NotMember Instance = new(); }
    public sealed record InvalidEmail : SendMiniLeagueInviteEmailResult { public static readonly InvalidEmail Instance = new(); }
}

public interface ISendMiniLeagueInviteEmailUseCase
{
    Task<SendMiniLeagueInviteEmailResult> ExecuteAsync(string userId, string leagueId, string email, CancellationToken ct);
}

public sealed class SendMiniLeagueInviteEmailUseCase : ISendMiniLeagueInviteEmailUseCase
{
    private readonly IMiniLeagueRepository _leagues;
    private readonly IMiniLeagueInviteRepository _invites;
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly AuthSettings _settings;
    private readonly Func<DateTimeOffset> _now;

    public SendMiniLeagueInviteEmailUseCase(
        IMiniLeagueRepository leagues, IMiniLeagueInviteRepository invites, IUserRepository users,
        ITokenService tokens, IEmailSender email, AuthSettings settings, Func<DateTimeOffset> now)
    {
        _leagues = leagues; _invites = invites; _users = users;
        _tokens = tokens; _email = email; _settings = settings; _now = now;
    }

    public async Task<SendMiniLeagueInviteEmailResult> ExecuteAsync(
        string userId, string leagueId, string email, CancellationToken ct)
    {
        var normalized = AuthValidation.NormalizeEmail(email);
        if (!AuthValidation.IsValidEmail(normalized))
            return SendMiniLeagueInviteEmailResult.InvalidEmail.Instance;

        var league = await _leagues.GetAsync(leagueId, ct);
        if (league is null) return SendMiniLeagueInviteEmailResult.LeagueNotFound.Instance;

        var members = await _leagues.GetMembersAsync(leagueId, ct);
        if (members.All(m => m.UserId != userId)) return SendMiniLeagueInviteEmailResult.NotMember.Instance;

        // Get-or-create, never regenerate: emailing a second person must not invalidate a link
        // already sent to an earlier recipient (unlike GenerateInviteUseCase, which always rotates).
        var invite = await _invites.GetByLeagueAsync(leagueId, ct);
        if (invite is null)
        {
            var token = _tokens.CreateInviteCode();
            invite = new MiniLeagueInvite(token, leagueId, userId, _now(), null);
            await _invites.AddAsync(invite, ct);
        }

        // The recipient may not have an account yet, so the inviter's own language is the only
        // signal available — people invite people who share their language.
        var inviter = await _users.GetByIdAsync(userId, ct);
        var inviterName = inviter?.DisplayName ?? string.Empty;
        var language = inviter?.Language ?? "is";

        var link = _settings.InviteUrlTemplate.Replace("{token}", invite.Token);
        await _email.SendMiniLeagueInviteEmailAsync(normalized, inviterName, league.Name, link, language, ct);

        return new SendMiniLeagueInviteEmailResult.Sent();
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~SendMiniLeagueInviteEmailUseCaseTests"`
Expected: PASS (6 tests).

- [ ] **Step 8: Add the dev default for `Auth:InviteUrlTemplate`**

In `Ez.Handball.Api/appsettings.Development.json`, add `"InviteUrlTemplate"` to the `Auth` section so the full section reads:

```json
  "Auth": {
    "RefreshTokenDays": 30,
    "EmailTokenHours": 24,
    "VerificationUrlTemplate": "http://localhost:5173/verify-email?token={token}",
    "ResetUrlTemplate": "http://localhost:5173/reset-password?token={token}",
    "InviteUrlTemplate": "http://localhost:5173/join?token={token}"
  },
```

- [ ] **Step 9: Run the full test suite**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add Ez.Handball.Application/Abstractions/AuthSettings.cs \
        Ez.Handball.Infrastructure/AuthInfrastructureRegistration.cs \
        Ez.Handball.Api/appsettings.Development.json \
        Ez.Handball.Tests/Application/UseCases/RegisterUseCaseTests.cs \
        Ez.Handball.Tests/Application/UseCases/ResendVerificationUseCaseTests.cs \
        Ez.Handball.Tests/Application/UseCases/RequestPasswordResetUseCaseTests.cs \
        Ez.Handball.Application/UseCases/SendMiniLeagueInviteEmailUseCase.cs \
        Ez.Handball.Tests/Application/UseCases/SendMiniLeagueInviteEmailUseCaseTests.cs
git commit -m "feat(mini-leagues): add use case to email a league invite without invalidating prior links"
```

---

## Task 5: `POST /api/mini-leagues/{id}/invite/email` endpoint

**Files:**
- Modify: `Ez.Handball.Api/MiniLeagueInviteEndpoints.cs`
- Modify: `Ez.Handball.Api/Program.cs`
- Test: `Ez.Handball.Tests/Api/Endpoints/MiniLeagueInviteEndpointTests.cs`

**Interfaces:**
- Consumes: `ISendMiniLeagueInviteEmailUseCase`, `SendMiniLeagueInviteEmailResult.*` (Task 4); `HttpContext.User.UserId()` (existing, `Ez.Handball.Api.Auth`); `"auth-sensitive"` rate-limit policy (existing, `Program.cs`).

- [ ] **Step 1: Write the failing endpoint tests**

In `Ez.Handball.Tests/Api/Endpoints/MiniLeagueInviteEndpointTests.cs`, add `using Ez.Handball.Application.UseCases;` is already present. Add a mock to the `Factory` class — change:

```csharp
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGenerateInviteUseCase> Generate { get; } = new();
        public Mock<IGetInviteUseCase> Get { get; } = new();
        public Mock<IPreviewInviteUseCase> Preview { get; } = new();
        public Mock<IJoinMiniLeagueUseCase> Join { get; } = new();
```

to:

```csharp
    public class Factory : WebApplicationFactory<Program>
    {
        public Mock<IGenerateInviteUseCase> Generate { get; } = new();
        public Mock<IGetInviteUseCase> Get { get; } = new();
        public Mock<IPreviewInviteUseCase> Preview { get; } = new();
        public Mock<IJoinMiniLeagueUseCase> Join { get; } = new();
        public Mock<ISendMiniLeagueInviteEmailUseCase> SendEmail { get; } = new();
```

and change:

```csharp
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(IGenerateInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IPreviewInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IJoinMiniLeagueUseCase)));
                services.AddSingleton(Generate.Object);
                services.AddSingleton(Get.Object);
                services.AddSingleton(Preview.Object);
                services.AddSingleton(Join.Object);
            });
```

to:

```csharp
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(IGenerateInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IGetInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IPreviewInviteUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(IJoinMiniLeagueUseCase)));
                services.Remove(services.Single(d => d.ServiceType == typeof(ISendMiniLeagueInviteEmailUseCase)));
                services.AddSingleton(Generate.Object);
                services.AddSingleton(Get.Object);
                services.AddSingleton(Preview.Object);
                services.AddSingleton(Join.Object);
                services.AddSingleton(SendEmail.Object);
            });
```

Also reset it in the constructor — change:

```csharp
    public MiniLeagueInviteEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Generate.Reset();
        _factory.Get.Reset();
        _factory.Preview.Reset();
        _factory.Join.Reset();
        _client = factory.CreateClient();
    }
```

to:

```csharp
    public MiniLeagueInviteEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Generate.Reset();
        _factory.Get.Reset();
        _factory.Preview.Reset();
        _factory.Join.Reset();
        _factory.SendEmail.Reset();
        _client = factory.CreateClient();
    }
```

Then add these test cases at the end of the class, before the final closing `}`:

```csharp
    [Fact]
    public async Task InviteEmail_WithoutToken_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/mini-leagues/lg-1/invite/email", new { email = "friend@example.com" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task InviteEmail_HappyPath_Returns200()
    {
        _factory.SendEmail.Setup(u => u.ExecuteAsync(It.IsAny<string>(), "lg-1", "friend@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMiniLeagueInviteEmailResult.Sent());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-1/invite/email", token, new { email = "friend@example.com" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("sent").GetBoolean());
    }

    [Fact]
    public async Task InviteEmail_BlankEmail_Returns400()
    {
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-1/invite/email", token, new { email = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_email", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InviteEmail_NotMember_Returns403()
    {
        _factory.SendEmail.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMiniLeagueInviteEmailResult.NotMember());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-1/invite/email", token, new { email = "friend@example.com" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_member", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InviteEmail_LeagueNotFound_Returns404()
    {
        _factory.SendEmail.Setup(u => u.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMiniLeagueInviteEmailResult.LeagueNotFound());
        var token = await TokenAsync();

        var resp = await _client.SendAsync(Req(HttpMethod.Post, "/api/mini-leagues/lg-x/invite/email", token, new { email = "friend@example.com" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("league_not_found", body.GetProperty("error").GetString());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~MiniLeagueInviteEndpointTests"`
Expected: FAIL to compile — `ISendMiniLeagueInviteEmailUseCase` isn't registered in `Program.cs` yet (or route doesn't exist).

- [ ] **Step 3: Add the endpoint**

In `Ez.Handball.Api/MiniLeagueInviteEndpoints.cs`, add a new request record next to the existing ones — change:

```csharp
public sealed record GenerateInviteRequest(int? ExpiresInDays);
public sealed record JoinMiniLeagueRequest(string? Token);
```

to:

```csharp
public sealed record GenerateInviteRequest(int? ExpiresInDays);
public sealed record SendInviteEmailRequest(string? Email);
public sealed record JoinMiniLeagueRequest(string? Token);
```

Then insert the new endpoint immediately after the existing `POST /{id}/invite` block (i.e. right after its closing `});` and before `group.MapGet("/{id}/invite", ...)`):

```csharp
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

- [ ] **Step 4: Register the use case in DI**

In `Ez.Handball.Api/Program.cs`, change:

```csharp
builder.Services.AddScoped<IJoinMiniLeagueUseCase, JoinMiniLeagueUseCase>();
```

to:

```csharp
builder.Services.AddScoped<IJoinMiniLeagueUseCase, JoinMiniLeagueUseCase>();
builder.Services.AddScoped<ISendMiniLeagueInviteEmailUseCase, SendMiniLeagueInviteEmailUseCase>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj --filter "FullyQualifiedName~MiniLeagueInviteEndpointTests"`
Expected: PASS (existing tests plus the 5 new ones).

- [ ] **Step 6: Run the full suite one more time**

Run: `dotnet test Ez.Handball.Tests/Ez.Handball.Tests.csproj`
Expected: PASS (baseline count + all new tests from Tasks 1-5; the 2 pre-existing unrelated `DebugReplayEndpointTests` failures are the only failures).

- [ ] **Step 7: Commit**

```bash
git add Ez.Handball.Api/MiniLeagueInviteEndpoints.cs Ez.Handball.Api/Program.cs Ez.Handball.Tests/Api/Endpoints/MiniLeagueInviteEndpointTests.cs
git commit -m "feat(mini-leagues): add POST /invite/email endpoint to email a league invite"
```

---

## Out of scope (per spec)

- Retry/queueing for failed sends.
- Tracking who was emailed an invite, or per-recipient invite tokens.
- Any languages beyond `is`/`en`.
- Fixing `GenerateInviteUseCase`'s existing always-regenerate behavior for the link-generation endpoint itself.
