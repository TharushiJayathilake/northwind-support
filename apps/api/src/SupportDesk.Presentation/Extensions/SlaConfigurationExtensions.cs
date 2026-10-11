using SupportDesk.Application.Features.Tickets.Triage;

namespace SupportDesk.Presentation.Extensions;

public static class SlaConfigurationExtensions
{
    public static IServiceCollection AddSlaSettings(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetRequiredSection(SlaSettings.SectionName).Get<SlaSettings>()
            ?? throw new InvalidOperationException("Sla configuration is missing.");
        settings.Validate();
        services.AddSingleton(settings);
        return services;
    }
}
