using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NexCode.Service.BackgroundServiceMode;

/// <summary>
/// Spec §29 — registers <c>NexCode.Gui.exe --background</c> in the
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> key so that
/// background service mode survives logon. Tests substitute a custom
/// <see cref="IRegistryRoot"/> so the round-trip can be verified without
/// touching the live registry.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupRegistration(IRegistryRoot? registryRoot = null)
{
    public const string DefaultValueName = "NexCode";
    public const string DefaultRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IRegistryRoot _registryRoot = registryRoot ?? new CurrentUserRegistryRoot();

    public Task EnableAsync(string executablePath, string arguments = "--background", string valueName = DefaultValueName)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("executablePath is required.", nameof(executablePath));
        }

        using var key = _registryRoot.OpenOrCreate(DefaultRunSubKey, writable: true);
        var command = $"\"{executablePath}\" {arguments}".Trim();
        key.SetValue(valueName, command, RegistryValueKind.String);
        return Task.CompletedTask;
    }

    public Task DisableAsync(string valueName = DefaultValueName)
    {
        using var key = _registryRoot.OpenOrCreate(DefaultRunSubKey, writable: true);
        key.DeleteValue(valueName, throwOnMissingValue: false);
        return Task.CompletedTask;
    }

    public Task<bool> IsEnabledAsync(string valueName = DefaultValueName)
    {
        using var key = _registryRoot.OpenOrCreate(DefaultRunSubKey, writable: false);
        var value = key.GetValue(valueName) as string;
        return Task.FromResult(!string.IsNullOrEmpty(value));
    }
}

/// <summary>Abstraction over <see cref="Registry"/> roots for tests.</summary>
[SupportedOSPlatform("windows")]
public interface IRegistryRoot
{
    IRegistryKey OpenOrCreate(string subKeyName, bool writable);
}

[SupportedOSPlatform("windows")]
public interface IRegistryKey : IDisposable
{
    object? GetValue(string valueName);
    void SetValue(string valueName, object value, RegistryValueKind kind);
    void DeleteValue(string valueName, bool throwOnMissingValue);
}

[SupportedOSPlatform("windows")]
public sealed class CurrentUserRegistryRoot : IRegistryRoot
{
    public IRegistryKey OpenOrCreate(string subKeyName, bool writable)
    {
        var key = Registry.CurrentUser.CreateSubKey(subKeyName, writable);
        return new RegistryKeyAdapter(key!);
    }

    private sealed class RegistryKeyAdapter(RegistryKey inner) : IRegistryKey
    {
        public void Dispose() => inner.Dispose();
        public object? GetValue(string valueName) => inner.GetValue(valueName);
        public void SetValue(string valueName, object value, RegistryValueKind kind) =>
            inner.SetValue(valueName, value, kind);
        public void DeleteValue(string valueName, bool throwOnMissingValue) =>
            inner.DeleteValue(valueName, throwOnMissingValue);
    }
}
