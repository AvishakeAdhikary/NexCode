using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace NexCode.Remote.Security;

/// <summary>
/// Spec §23.3 — Bearer token validation for nexcode-remote.
///
/// Tokens are issued by Azure AD (MSAL); the audience must equal
/// <c>NEXCODE_AAD_CLIENT_ID</c>. The authority is read from
/// <c>Auth:Authority</c> (e.g. <c>https://login.microsoftonline.com/{tenant}/v2.0</c>).
/// </summary>
public static class MsalJwtValidator
{
    public const string AadClientIdEnv = "NEXCODE_AAD_CLIENT_ID";
    public const string AuthenticationScheme = JwtBearerDefaults.AuthenticationScheme;

    public static AuthenticationBuilder AddMsalJwtBearer(this IServiceCollection services, IConfiguration configuration)
    {
        var authoritySection = configuration.GetSection("Auth");
        var authority = authoritySection["Authority"]
            ?? "https://login.microsoftonline.com/common/v2.0";
        var audience = Environment.GetEnvironmentVariable(AadClientIdEnv)
            ?? authoritySection["ClientId"]
            ?? string.Empty;

        var builder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = !string.Equals(
                    Environment.GetEnvironmentVariable("NEXCODE_REMOTE_DEV_INSECURE"),
                    "1",
                    StringComparison.Ordinal);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrEmpty(authority),
                    ValidateAudience = !string.IsNullOrEmpty(audience),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAudience = audience,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireMsalJwt", policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        return builder;
    }

    /// <summary>Pure helper used in tests to validate the audience field of a parsed token.</summary>
    public static bool ValidateAudience(string? tokenAudience, string expectedAudience)
    {
        if (string.IsNullOrEmpty(expectedAudience))
        {
            return true;
        }

        return string.Equals(tokenAudience, expectedAudience, StringComparison.Ordinal);
    }
}
