using Microsoft.Extensions.DependencyInjection;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Services;

/// <summary>
/// Registers GUI-process-scoped services and view models with the DI container.
/// Called from <c>App.xaml.cs</c> at startup.
/// </summary>
public static class GuiServiceCollection
{
    public static IServiceCollection AddNexCodeGuiServices(this IServiceCollection services)
    {
        // Singletons: process-wide services
        services.AddSingleton<ThemeService>();
        services.AddSingleton<HelperControlClient>();

        // Top-level view models — singletons because they hold the shell state
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ProviderSettingsViewModel>();

        // Per-resolution view models that may be spawned multiple times
        services.AddTransient<SessionViewModel>();
        services.AddTransient<PlanViewModel>();
        services.AddTransient<TodoListViewModel>();
        services.AddTransient<ClarifyQuestionViewModel>();
        services.AddTransient<PermissionPromptViewModel>();
        services.AddTransient<CheckpointViewModel>();
        services.AddTransient<ProjectViewModel>();

        return services;
    }
}
