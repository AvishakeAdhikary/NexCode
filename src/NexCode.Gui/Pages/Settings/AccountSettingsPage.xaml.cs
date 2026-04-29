using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Account / subscription / sign-out panel (spec §32). The super-user developer panel is hidden
/// by default; enable it by calling <see cref="SetSuperUser"/> from a host once the helper
/// reports a super-user claim.
/// </summary>
public sealed partial class AccountSettingsPage : Page
{
    public AccountSettingsPage()
    {
        InitializeComponent();
    }

    public void SetSuperUser(bool isSuperUser)
    {
        SuperUserPanel.Visibility = isSuperUser ? Visibility.Visible : Visibility.Collapsed;
    }
}
