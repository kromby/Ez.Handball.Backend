using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ez.Handball.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    // JwtBearer is configured with MapInboundClaims = false (Task 18), so "sub" stays "sub".
    public static string? UserId(this ClaimsPrincipal principal)
        => principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
}
