using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddDeskAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        settings.Validate();
        services.AddSingleton(settings);
        services.AddScoped<JwtTokens>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = settings.Issuer,
                ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero,
                NameClaimType = "name",
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var auth = context.HttpContext.RequestServices.GetRequiredService<IAgentAuthentication>();
                    if (!int.TryParse(context.Principal?.FindFirstValue("sub"), out var id) ||
                        await auth.GetActiveAsync(id, context.HttpContext.RequestAborted) is not { } agent)
                    {
                        context.Fail("Invalid session.");
                        return;
                    }
                    // Derive audit identity from the current database record, not submitted JSON.
                    if (context.Principal?.Identity is not ClaimsIdentity identity)
                    {
                        context.Fail("Invalid session.");
                        return;
                    }
                    foreach (var claim in identity.FindAll("name").ToArray()) identity.RemoveClaim(claim);
                    identity.AddClaim(new Claim("name", agent.FullName));
                },
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        return services;
    }
}
