using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Presentation.Extensions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Presentation;

public sealed class SlaConfigurationTests
{
    [Fact]
    public void Registration_BindsExistingDevelopmentSettingsAndResolvesPolicy()
    {
        var config = DevelopmentConfiguration().Build();
        var services = new ServiceCollection();
        services.AddSlaSettings(config).AddApplication();
        using var provider = services.BuildServiceProvider();
        var result = provider.GetRequiredService<TicketTriagePolicy>().CalculateDeadline(
            TicketPriority.Critical, CustomerTier.Premium, FixedClock.DefaultNow);
        Assert.Equal(TimeSpan.FromHours(2), result.Window);
        Assert.Equal(25, provider.GetRequiredService<SlaSettings>().AtRiskThresholdPercent);
    }

    [Fact]
    public void Registration_MissingSectionFailsImmediately()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddSlaSettings(new ConfigurationBuilder().Build()));
        Assert.Contains("Sla", error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("not-a-number")]
    public void Registration_InvalidConfigurationFailsImmediately(string value)
    {
        var config = DevelopmentConfiguration().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sla:BaseWindowHours:Critical"] = value
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSlaSettings(config));
        Assert.Contains("Sla", error.Message);
    }

    private static IConfigurationBuilder DevelopmentConfiguration() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "sla-appsettings.json"));
}
