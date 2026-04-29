using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;
using NexCode.Service.BackgroundServiceMode;

namespace NexCode.Cli.Tests.BackgroundServiceMode;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistrationTests
{
    [Fact]
    public async Task EnableThenIsEnabled_ReturnsTrue()
    {
        var root = new InMemoryRegistryRoot();
        var registration = new StartupRegistration(root);

        Assert.False(await registration.IsEnabledAsync());

        await registration.EnableAsync(@"C:\\Program Files\\NexCode\\NexCode.Gui.exe");

        Assert.True(await registration.IsEnabledAsync());
        var stored = root.OpenOrCreate(StartupRegistration.DefaultRunSubKey, true).GetValue(StartupRegistration.DefaultValueName) as string;
        Assert.NotNull(stored);
        Assert.Contains("NexCode.Gui.exe", stored!);
        Assert.Contains("--background", stored!);
    }

    [Fact]
    public async Task DisableAfterEnable_ReturnsFalse()
    {
        var root = new InMemoryRegistryRoot();
        var registration = new StartupRegistration(root);

        await registration.EnableAsync(@"C:\\NexCode\\NexCode.Gui.exe");
        Assert.True(await registration.IsEnabledAsync());

        await registration.DisableAsync();
        Assert.False(await registration.IsEnabledAsync());
    }

    [Fact]
    public async Task Enable_ThrowsOnEmptyPath()
    {
        var registration = new StartupRegistration(new InMemoryRegistryRoot());
        await Assert.ThrowsAsync<ArgumentException>(() => registration.EnableAsync(""));
    }

    [SupportedOSPlatform("windows")]
    private sealed class InMemoryRegistryRoot : IRegistryRoot
    {
        private readonly Dictionary<string, InMemoryRegistryKey> _keys = new(StringComparer.OrdinalIgnoreCase);

        public IRegistryKey OpenOrCreate(string subKeyName, bool writable)
        {
            if (!_keys.TryGetValue(subKeyName, out var key))
            {
                key = new InMemoryRegistryKey();
                _keys[subKeyName] = key;
            }
            return key;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class InMemoryRegistryKey : IRegistryKey
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        public object? GetValue(string valueName)
        {
            return _values.TryGetValue(valueName, out var value) ? value : null;
        }

        public void SetValue(string valueName, object value, RegistryValueKind kind)
        {
            _values[valueName] = value;
        }

        public void DeleteValue(string valueName, bool throwOnMissingValue)
        {
            if (!_values.Remove(valueName) && throwOnMissingValue)
            {
                throw new ArgumentException("Value not found.", nameof(valueName));
            }
        }

        public void Dispose()
        {
        }
    }
}
