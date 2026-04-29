using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexCode.Remote.Security;

namespace NexCode.Cli.Tests.Remote;

public sealed class MsalJwtValidatorTests
{
    [Fact]
    public void ValidateAudience_ReturnsTrue_WhenExpectedMatchesToken()
    {
        Assert.True(MsalJwtValidator.ValidateAudience("nexcode-aad-id", "nexcode-aad-id"));
    }

    [Fact]
    public void ValidateAudience_ReturnsFalse_WhenAudienceMismatch()
    {
        Assert.False(MsalJwtValidator.ValidateAudience("other-id", "nexcode-aad-id"));
    }

    [Fact]
    public void ValidateAudience_AllowsEmptyExpected_AsTrustOpenConfig()
    {
        Assert.True(MsalJwtValidator.ValidateAudience("anything", string.Empty));
    }

    [Fact]
    public void AddMsalJwtBearer_WiresAuthorityAndAudience_FromEnvironment()
    {
        Environment.SetEnvironmentVariable(MsalJwtValidator.AadClientIdEnv, "nexcode-test-aud");
        Environment.SetEnvironmentVariable("NEXCODE_REMOTE_DEV_INSECURE", "1");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:Authority"] = "https://login.microsoftonline.com/contoso/v2.0"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMsalJwtBearer(config);

            using var provider = services.BuildServiceProvider();
            var optionsSnapshot = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
            var options = optionsSnapshot.Get(JwtBearerDefaults.AuthenticationScheme);

            Assert.Equal("https://login.microsoftonline.com/contoso/v2.0", options.Authority);
            Assert.Equal("nexcode-test-aud", options.TokenValidationParameters.ValidAudience);
            Assert.True(options.TokenValidationParameters.ValidateAudience);
            Assert.False(options.RequireHttpsMetadata);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MsalJwtValidator.AadClientIdEnv, null);
            Environment.SetEnvironmentVariable("NEXCODE_REMOTE_DEV_INSECURE", null);
        }
    }
}
