using SupportDesk.Presentation.Authentication;

namespace SupportDesk.UnitTests.Presentation;

public sealed class JwtSettingsTests
{
    [Theory]
    [InlineData("", "Issuer", "Audience", 60)]
    [InlineData("short", "Issuer", "Audience", 60)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "", "Audience", 60)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "Issuer", "", 60)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "Issuer", "Audience", 0)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "Issuer", "Audience", 1441)]
    public void InvalidConfigurationFailsClearly(string key, string issuer, string audience, int minutes)
    {
        var settings = new JwtSettings { SigningKey = key, Issuer = issuer, Audience = audience, LifetimeMinutes = minutes };
        Assert.Throws<InvalidOperationException>(settings.Validate);
    }
}
