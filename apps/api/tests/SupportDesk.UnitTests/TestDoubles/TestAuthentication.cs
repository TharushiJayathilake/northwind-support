using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Abstractions;
using SupportDesk.Infrastructure.Services;
using SupportDesk.Presentation.Authentication;

namespace SupportDesk.UnitTests.TestDoubles;

public static class TestAuthentication
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"] = "Tests", ["Jwt:Audience"] = "TestClient", ["Jwt:LifetimeMinutes"] = "60"
        });
        builder.Services.AddScoped<IAgentAuthentication, AgentAuthentication>();
        builder.Services.AddDeskAuthentication(builder.Configuration);
        builder.Services.AddScoped(sp => new JwtTokens(sp.GetRequiredService<JwtSettings>(), new SystemClock()));
    }

    public static void SignIn(HttpClient client, IServiceProvider services, int id, string name)
    {
        var token = new JwtTokens(services.GetRequiredService<JwtSettings>(), new SystemClock())
            .Create(new AuthAgent(id, name, "test@example.test"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }
}
