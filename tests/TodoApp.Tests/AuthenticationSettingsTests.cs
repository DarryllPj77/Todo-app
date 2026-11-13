using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TodoApp.Api.Configuration;

namespace TodoApp.Tests;

public sealed class AuthenticationSettingsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    public void ShortKeys_AreRejected(int byteLength)
    {
        Assert.False(new JwtSettings { SigningKey = Convert.ToBase64String(new byte[byteLength]) }.IsValid());
    }

    [Fact]
    public void KeyAndLifetimeBounds_AreValidated()
    {
        Assert.False(new JwtSettings { SigningKey = "not-base64" }.IsValid());
        Assert.False(new JwtSettings { SigningKey = TestAuthentication.SigningKey, AccessTokenMinutes = 0 }.IsValid());
        Assert.False(new JwtSettings { SigningKey = TestAuthentication.SigningKey, AccessTokenMinutes = 61 }.IsValid());
        Assert.True(new JwtSettings { SigningKey = TestAuthentication.SigningKey }.IsValid());
    }

    [Fact]
    public async Task MissingSigningKey_StopsStartupWithoutGeneratingAnImplicitKey()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoDatabase"] = "Host=localhost;Database=unused;Username=unused",
                ["Jwt:SigningKey"] = null
            })));
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
