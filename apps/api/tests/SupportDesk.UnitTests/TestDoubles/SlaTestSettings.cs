using Microsoft.Extensions.Configuration;
using SupportDesk.Application.Features.Tickets.Triage;

namespace SupportDesk.UnitTests.TestDoubles;

public static class SlaTestSettings
{
    public static SlaSettings Create() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "sla-appsettings.json"))
        .Build().GetRequiredSection(SlaSettings.SectionName).Get<SlaSettings>()!;
}
