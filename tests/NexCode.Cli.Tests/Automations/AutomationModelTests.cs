using System.Text.Json;
using NexCode.Service.Automations;

namespace NexCode.Cli.Tests.Automations;

public sealed class AutomationModelTests
{
    [Fact]
    public void Trigger_RoundTrips_Schedule()
    {
        AutomationTrigger trigger = new ScheduleTrigger("0 */5 * * * *");
        var json = AutomationModel.SerializeTrigger(trigger);
        var parsed = AutomationModel.ParseTrigger(json);

        var schedule = Assert.IsType<ScheduleTrigger>(parsed);
        Assert.Equal("0 */5 * * * *", schedule.Cron);
    }

    [Fact]
    public void Trigger_RoundTrips_FileChange()
    {
        AutomationTrigger trigger = new FileChangeTrigger(@"C:\\repos\\app", "*.cs", 500);
        var json = AutomationModel.SerializeTrigger(trigger);
        var parsed = AutomationModel.ParseTrigger(json);

        var fc = Assert.IsType<FileChangeTrigger>(parsed);
        Assert.Equal("*.cs", fc.Glob);
        Assert.Equal(500, fc.DebounceMs);
    }

    [Fact]
    public void Trigger_RoundTrips_GitEvent()
    {
        AutomationTrigger trigger = new GitEventTrigger("push");
        var json = AutomationModel.SerializeTrigger(trigger);
        var parsed = AutomationModel.ParseTrigger(json);

        var git = Assert.IsType<GitEventTrigger>(parsed);
        Assert.Equal("push", git.EventType);
    }

    [Fact]
    public void Trigger_RoundTrips_SessionEnd_Manual_Webhook()
    {
        Assert.IsType<SessionEndTrigger>(
            AutomationModel.ParseTrigger(AutomationModel.SerializeTrigger(new SessionEndTrigger())));
        Assert.IsType<ManualTrigger>(
            AutomationModel.ParseTrigger(AutomationModel.SerializeTrigger(new ManualTrigger())));

        var webhook = AutomationModel.ParseTrigger(
            AutomationModel.SerializeTrigger(new WebhookTrigger("/hooks/build")));
        var asWebhook = Assert.IsType<WebhookTrigger>(webhook);
        Assert.Equal("/hooks/build", asWebhook.Path);
    }

    [Fact]
    public void Trigger_UnknownKind_DefaultsToManual()
    {
        const string json = """{ "kind": "what_is_this", "field": 1 }""";
        var parsed = AutomationModel.ParseTrigger(json);
        Assert.IsType<ManualTrigger>(parsed);
    }

    [Fact]
    public void Steps_RoundTrip_AllShapes()
    {
        AutomationStep[] steps =
        [
            new RunCommandStep("echo hi", "powershell", null, 30),
            new SendNotificationStep("Done", "Build green"),
            new CallWebhookStep("https://example.com/hook", "POST", null, "{}"),
            new GitActionStep(@"C:\\repos\\app", "status", null),
            new WaitStep(250),
            new RunSessionStep(@"C:\\repos\\app", "Bump version", "code")
        ];

        var json = AutomationModel.SerializeSteps(steps);
        var parsed = AutomationModel.ParseSteps(json);

        Assert.Equal(steps.Length, parsed.Length);
        Assert.IsType<RunCommandStep>(parsed[0]);
        Assert.IsType<SendNotificationStep>(parsed[1]);
        Assert.IsType<CallWebhookStep>(parsed[2]);
        Assert.IsType<GitActionStep>(parsed[3]);
        Assert.IsType<WaitStep>(parsed[4]);
        Assert.IsType<RunSessionStep>(parsed[5]);
    }

    [Fact]
    public void Steps_Conditional_NestedStepsParse()
    {
        AutomationStep[] steps =
        [
            new ConditionalStep(
                "true",
                [new WaitStep(10)],
                [new SendNotificationStep("else", "branch")])
        ];

        var json = AutomationModel.SerializeSteps(steps);
        var parsed = AutomationModel.ParseSteps(json);

        var cond = Assert.IsType<ConditionalStep>(Assert.Single(parsed));
        Assert.Single(cond.ThenSteps);
        Assert.Single(cond.ElseSteps);
        Assert.IsType<WaitStep>(cond.ThenSteps[0]);
        Assert.IsType<SendNotificationStep>(cond.ElseSteps[0]);
    }

    [Fact]
    public void Steps_UnknownKind_IsDropped()
    {
        const string json = """[ { "kind": "fly_to_moon" } ]""";
        var parsed = AutomationModel.ParseSteps(json);
        Assert.Empty(parsed);
    }

    [Fact]
    public void Steps_EmptyJson_ReturnsEmpty()
    {
        Assert.Empty(AutomationModel.ParseSteps(""));
        Assert.Empty(AutomationModel.ParseSteps("null"));
    }

    [Fact]
    public void Trigger_KindFieldPresentInSerializedJson()
    {
        var json = AutomationModel.SerializeTrigger(new ScheduleTrigger("* * * * *"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("schedule", doc.RootElement.GetProperty("kind").GetString());
    }
}
