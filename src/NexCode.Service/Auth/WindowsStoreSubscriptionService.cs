using System.Runtime.Versioning;
using Windows.Services.Store;

namespace NexCode.Service.Auth;

[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsStoreSubscriptionService(
    ILogger<WindowsStoreSubscriptionService> logger) : IStoreSubscriptionService
{
    public async Task<StoreSubscriptionRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var storeContext = StoreContext.GetDefault();
            var appLicense = await storeContext.GetAppLicenseAsync();

            cancellationToken.ThrowIfCancellationRequested();

            var productIds = appLicense.AddOnLicenses.Values
                .Select(license => license.InAppOfferToken)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new StoreSubscriptionRefreshResult(
                Succeeded: true,
                ProductIds: productIds,
                Source: "windows-store",
                Warning: null,
                ErrorMessage: null,
                ReceiptJson: string.IsNullOrWhiteSpace(appLicense.ExtendedJsonData)
                    ? "{}"
                    : appLicense.ExtendedJsonData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Microsoft Store license refresh failed.");

            return new StoreSubscriptionRefreshResult(
                Succeeded: false,
                ProductIds: [],
                Source: "windows-store",
                Warning: "Microsoft Store validation is currently unavailable.",
                ErrorMessage: ex.Message,
                ReceiptJson: "{}");
        }
    }
}
