using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets;

public sealed class TicketTriagePolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Low, CustomerTier.Standard, 72)]
    [InlineData(TicketPriority.Medium, CustomerTier.Standard, 24)]
    [InlineData(TicketPriority.High, CustomerTier.Standard, 8)]
    [InlineData(TicketPriority.Critical, CustomerTier.Standard, 4)]
    [InlineData(TicketPriority.Low, CustomerTier.Premium, 36)]
    [InlineData(TicketPriority.Medium, CustomerTier.Premium, 12)]
    [InlineData(TicketPriority.High, CustomerTier.Premium, 4)]
    [InlineData(TicketPriority.Critical, CustomerTier.Premium, 2)]
    public void Calculate_UsesRequestedPriorityAndTierWindow(TicketPriority priority, CustomerTier tier, double hours)
    {
        var clock = new FixedClock();
        var decision = new TicketTriagePolicy(Settings()).Calculate(priority, Category(), tier, clock.UtcNow);

        Assert.Equal(priority, decision.Priority);
        Assert.Equal(TimeSpan.FromHours(hours), decision.Window);
        Assert.Equal(clock.UtcNow.AddHours(hours), decision.DueAtUtc);
        Assert.Equal(DateTimeKind.Utc, decision.DueAtUtc.Kind);
    }

    [Fact]
    public void Calculate_WithoutRequestedPriority_DefaultsToMedium()
    {
        var result = new TicketTriagePolicy(Settings()).Calculate(null, Category(), CustomerTier.Standard, FixedClock.DefaultNow);
        Assert.Equal(TicketPriority.Medium, result.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(24), result.DueAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    public void Calculate_CategoryFlagForcesCritical(TicketPriority? requested)
    {
        var result = new TicketTriagePolicy(Settings()).Calculate(requested, Category(true), CustomerTier.Premium, FixedClock.DefaultNow);
        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(2), result.DueAtUtc);
    }

    [Theory]
    [InlineData(CustomerTier.Standard)]
    [InlineData(CustomerTier.Premium)]
    public void CalculateDeadline_AppliesConfiguredMinimum(CustomerTier tier)
    {
        var settings = Settings();
        settings.BaseWindowHours[TicketPriority.Critical] = 0.5;
        var result = new TicketTriagePolicy(settings).CalculateDeadline(TicketPriority.Critical, tier, FixedClock.DefaultNow);
        Assert.Equal(TimeSpan.FromHours(1), result.Window);
        Assert.Equal(FixedClock.DefaultNow.AddHours(1), result.DueAtUtc);
    }

    [Fact]
    public void CalculateDeadline_SupportsFractionalHoursAndExplicitStartTime()
    {
        var settings = Settings();
        settings.BaseWindowHours[TicketPriority.High] = 5;
        var clock = new FixedClock();
        clock.Advance(TimeSpan.FromMinutes(37));
        var result = new TicketTriagePolicy(settings).CalculateDeadline(TicketPriority.High, CustomerTier.Premium, clock.UtcNow);
        Assert.Equal(TimeSpan.FromMinutes(150), result.Window);
        Assert.Equal(new DateTime(2026, 3, 2, 12, 7, 0, DateTimeKind.Utc), result.DueAtUtc);
    }

    [Fact]
    public void CalculateDeadline_UsesCustomMultiplierAndFloor()
    {
        var settings = Settings();
        settings.PremiumCustomerMultiplier = 0.25;
        settings.MinimumWindowHours = 1.5;
        var result = new TicketTriagePolicy(settings).CalculateDeadline(TicketPriority.Critical, CustomerTier.Premium, FixedClock.DefaultNow);
        Assert.Equal(TimeSpan.FromMinutes(90), result.Window);
    }

    [Fact]
    public void CalculateDeadline_RejectsNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => new TicketTriagePolicy(Settings()).CalculateDeadline(
            TicketPriority.High, CustomerTier.Standard, DateTime.SpecifyKind(FixedClock.DefaultNow, DateTimeKind.Unspecified)));
    }

    [Theory]
    [InlineData("missingPriority")]
    [InlineData("zeroWindow")]
    [InlineData("negativeWindow")]
    [InlineData("nanWindow")]
    [InlineData("infiniteWindow")]
    [InlineData("zeroMultiplier")]
    [InlineData("largeMultiplier")]
    [InlineData("nanMultiplier")]
    [InlineData("zeroMinimum")]
    [InlineData("infiniteMinimum")]
    [InlineData("zeroThreshold")]
    [InlineData("largeThreshold")]
    [InlineData("nanThreshold")]
    public void Settings_InvalidValuesFailClearly(string invalid)
    {
        var settings = Settings();
        switch (invalid)
        {
            case "missingPriority": settings.BaseWindowHours.Remove(TicketPriority.Low); break;
            case "zeroWindow": settings.BaseWindowHours[TicketPriority.Low] = 0; break;
            case "negativeWindow": settings.BaseWindowHours[TicketPriority.Low] = -1; break;
            case "nanWindow": settings.BaseWindowHours[TicketPriority.Low] = double.NaN; break;
            case "infiniteWindow": settings.BaseWindowHours[TicketPriority.Low] = double.PositiveInfinity; break;
            case "zeroMultiplier": settings.PremiumCustomerMultiplier = 0; break;
            case "largeMultiplier": settings.PremiumCustomerMultiplier = 2; break;
            case "nanMultiplier": settings.PremiumCustomerMultiplier = double.NaN; break;
            case "zeroMinimum": settings.MinimumWindowHours = 0; break;
            case "infiniteMinimum": settings.MinimumWindowHours = double.PositiveInfinity; break;
            case "zeroThreshold": settings.AtRiskThresholdPercent = 0; break;
            case "largeThreshold": settings.AtRiskThresholdPercent = 101; break;
            case "nanThreshold": settings.AtRiskThresholdPercent = double.NaN; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => new TicketTriagePolicy(settings));
        Assert.Contains("Sla:", error.Message);
    }

    [Fact]
    public void Settings_MissingValuesFailClearly()
    {
        Assert.Throws<InvalidOperationException>(() => new TicketTriagePolicy(new SlaSettings()));
    }

    [Fact]
    public void Policy_SnapshotsSettingsToPreventLaterMutation()
    {
        var settings = Settings();
        var policy = new TicketTriagePolicy(settings);
        settings.BaseWindowHours[TicketPriority.High] = 100;
        Assert.Equal(TimeSpan.FromHours(8), policy.CalculateDeadline(TicketPriority.High, CustomerTier.Standard, FixedClock.DefaultNow).Window);
    }

    private static Category Category(bool forcesCritical = false) => new("Arbitrary category", false, forcesCritical);

    private static SlaSettings Settings() => new()
    {
        BaseWindowHours = new()
        {
            [TicketPriority.Low] = 72, [TicketPriority.Medium] = 24,
            [TicketPriority.High] = 8, [TicketPriority.Critical] = 4
        },
        PremiumCustomerMultiplier = 0.5,
        MinimumWindowHours = 1,
        AtRiskThresholdPercent = 25
    };
}
