using NexCode.Service.Plugins;

namespace NexCode.Cli.Tests.Plugins;

public sealed class PluginManifestTests
{
    [Fact]
    public void TryParse_ParsesValidManifest()
    {
        const string json = """
            {
              "name": "PrComment",
              "version": "1.0.0",
              "author": "ACME",
              "entry_point": "main.exe",
              "hooks": ["on_session_start", "on_session_end"],
              "permissions": ["fs:read"]
            }
        """;

        var manifest = PluginManifest.TryParse(json, out var error);

        Assert.NotNull(manifest);
        Assert.Null(error);
        Assert.Equal("PrComment", manifest!.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("main.exe", manifest.EntryPoint);
        Assert.Equal(2, manifest.Hooks.Length);
        Assert.Single(manifest.Permissions);
    }

    [Fact]
    public void TryParse_RejectsMissingName()
    {
        const string json = """{ "version": "1.0", "entry_point": "x" }""";

        var manifest = PluginManifest.TryParse(json, out var error);

        Assert.Null(manifest);
        Assert.Equal("manifest_missing_name", error);
    }

    [Fact]
    public void TryParse_RejectsMissingVersion()
    {
        const string json = """{ "name": "Foo", "entry_point": "x" }""";

        var manifest = PluginManifest.TryParse(json, out var error);

        Assert.Null(manifest);
        Assert.Equal("manifest_missing_version", error);
    }

    [Fact]
    public void TryParse_RejectsMissingEntryPoint()
    {
        const string json = """{ "name": "Foo", "version": "1.0" }""";

        var manifest = PluginManifest.TryParse(json, out var error);

        Assert.Null(manifest);
        Assert.Equal("manifest_missing_entry_point", error);
    }

    [Fact]
    public void TryParse_RejectsUnknownHook()
    {
        const string json = """
            {
              "name": "Foo",
              "version": "1.0",
              "entry_point": "main.exe",
              "hooks": ["on_unknown_hook"]
            }
        """;

        var manifest = PluginManifest.TryParse(json, out var error);

        Assert.Null(manifest);
        Assert.Equal("manifest_unknown_hook:on_unknown_hook", error);
    }

    [Fact]
    public void TryParse_AllowsAllSpecHooks()
    {
        var hooks = string.Join(", ", PluginManifest.AllowedHooks.Select(h => $"\"{h}\""));
        var json = $$"""
            {
              "name": "Foo",
              "version": "1.0",
              "entry_point": "main.exe",
              "hooks": [{{hooks}}]
            }
        """;

        var manifest = PluginManifest.TryParse(json, out var error);

        Assert.NotNull(manifest);
        Assert.Null(error);
        Assert.Equal(PluginManifest.AllowedHooks.Length, manifest!.Hooks.Length);
    }

    [Fact]
    public void TryParse_RejectsInvalidJson()
    {
        var manifest = PluginManifest.TryParse("not json", out var error);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.StartsWith("manifest_invalid_json", error);
    }

    [Fact]
    public void TryParse_TreatsMissingHooksAsEmpty()
    {
        const string json = """{ "name": "Foo", "version": "1.0", "entry_point": "main.exe" }""";

        var manifest = PluginManifest.TryParse(json, out _);

        Assert.NotNull(manifest);
        Assert.Empty(manifest!.Hooks);
        Assert.Empty(manifest.Permissions);
    }
}
