using System.Text;

namespace SupportDesk.Presentation.Authentication;

public sealed class JwtSettings
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 60;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32 || string.IsNullOrWhiteSpace(Issuer) ||
            string.IsNullOrWhiteSpace(Audience) || LifetimeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Jwt configuration requires a signing key of at least 32 bytes, issuer, audience and lifetime of 1-1440 minutes.");
    }
}
