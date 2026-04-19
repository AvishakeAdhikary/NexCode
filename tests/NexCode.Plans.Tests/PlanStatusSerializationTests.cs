using System.Text.Json;
using NexCode.Shared.Json;
using NexCode.Shared.Models;

namespace NexCode.Plans.Tests;

public sealed class PlanStatusSerializationTests
{
    [Fact]
    public void JsonSerialization_WritesPlanStatusAsCamelCaseString()
    {
        var json = JsonSerializer.Serialize(PlanStatus.PendingConfirmation, JsonSerialization.Options);

        Assert.Equal("\"pendingConfirmation\"", json);
    }
}
