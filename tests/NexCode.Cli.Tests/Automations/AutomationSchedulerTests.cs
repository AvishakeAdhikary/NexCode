using NexCode.Service.Automations;

namespace NexCode.Cli.Tests.Automations;

public sealed class AutomationSchedulerTests
{
    [Fact]
    public void ComputeNextFire_FiveMinuteCron_ReturnsNextSlot()
    {
        var now = new DateTime(2026, 4, 29, 12, 1, 0, DateTimeKind.Utc);

        var next = AutomationScheduler.ComputeNextFire("*/5 * * * *", now);

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2026, 4, 29, 12, 5, 0, DateTimeKind.Utc), next!.Value.UtcDateTime);
    }

    [Fact]
    public void ComputeNextFire_HourlyCron_RollsOverHour()
    {
        var now = new DateTime(2026, 4, 29, 12, 30, 0, DateTimeKind.Utc);

        var next = AutomationScheduler.ComputeNextFire("0 * * * *", now);

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2026, 4, 29, 13, 0, 0, DateTimeKind.Utc), next!.Value.UtcDateTime);
    }

    [Fact]
    public void ComputeNextFire_DailyAtMidnight_RollsOverDay()
    {
        var now = new DateTime(2026, 4, 29, 23, 59, 0, DateTimeKind.Utc);

        var next = AutomationScheduler.ComputeNextFire("0 0 * * *", now);

        Assert.NotNull(next);
        Assert.Equal(new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), next!.Value.UtcDateTime);
    }

    [Fact]
    public void ComputeNextFire_SecondsFormat_Supported()
    {
        var now = new DateTime(2026, 4, 29, 12, 0, 0, DateTimeKind.Utc);

        var next = AutomationScheduler.ComputeNextFire("*/30 * * * * *", now);

        Assert.NotNull(next);
        Assert.True(next!.Value.UtcDateTime > now);
        Assert.True(next.Value.UtcDateTime <= now.AddSeconds(30));
    }

    [Fact]
    public void ComputeNextFire_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(AutomationScheduler.ComputeNextFire("", DateTime.UtcNow));
        Assert.Null(AutomationScheduler.ComputeNextFire("   ", DateTime.UtcNow));
    }

    [Fact]
    public void ComputeNextFire_InvalidCron_Throws()
    {
        Assert.ThrowsAny<Exception>(() =>
            AutomationScheduler.ComputeNextFire("not a cron", DateTime.UtcNow));
    }

    [Fact]
    public void WebhookPort_Defaults_To_51723()
    {
        // Make sure the env var isn't set so the default applies in this assertion.
        Environment.SetEnvironmentVariable("NEXCODE_AUTOMATION_PORT", null);
        Assert.Equal(AutomationScheduler.DefaultWebhookPort, AutomationScheduler.WebhookPort);
        Assert.Equal(51723, AutomationScheduler.DefaultWebhookPort);
    }
}
