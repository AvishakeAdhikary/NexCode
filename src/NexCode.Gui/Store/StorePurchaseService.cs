using System.Runtime.Versioning;
using Windows.Services.Store;
using WinRT.Interop;

namespace NexCode.Gui.Store;

internal sealed record StorePurchaseResult(
    StorePurchaseStatus Status,
    string Message,
    bool ShouldRefreshSubscription);

[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class StorePurchaseService
{
    public async Task<StorePurchaseResult> RequestPurchaseAsync(
        string productId,
        IntPtr ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        StoreContext storeContext;

        try
        {
            storeContext = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(storeContext, ownerWindowHandle);
        }
        catch (Exception ex)
        {
            return new StorePurchaseResult(
                Status: StorePurchaseStatus.ServerError,
                Message: $"Microsoft Store purchase is unavailable in this app context: {ex.Message}",
                ShouldRefreshSubscription: false);
        }

        var result = await storeContext.RequestPurchaseAsync(productId);
        cancellationToken.ThrowIfCancellationRequested();

        return result.Status switch
        {
            StorePurchaseStatus.Succeeded => new StorePurchaseResult(
                Status: result.Status,
                Message: "Microsoft Store purchase completed successfully. Restoring purchases now...",
                ShouldRefreshSubscription: true),
            StorePurchaseStatus.AlreadyPurchased => new StorePurchaseResult(
                Status: result.Status,
                Message: "This add-on is already owned by the current Microsoft Store account. Restoring purchases now...",
                ShouldRefreshSubscription: true),
            StorePurchaseStatus.NotPurchased => new StorePurchaseResult(
                Status: result.Status,
                Message: "The Microsoft Store purchase dialog was closed before the purchase completed.",
                ShouldRefreshSubscription: false),
            StorePurchaseStatus.NetworkError => new StorePurchaseResult(
                Status: result.Status,
                Message: "The Microsoft Store purchase could not complete because the network is unavailable.",
                ShouldRefreshSubscription: false),
            StorePurchaseStatus.ServerError => new StorePurchaseResult(
                Status: result.Status,
                Message: "The Microsoft Store purchase could not complete because the Store service returned an error.",
                ShouldRefreshSubscription: false),
            _ => new StorePurchaseResult(
                Status: result.Status,
                Message: "The Microsoft Store purchase returned an unexpected status.",
                ShouldRefreshSubscription: false)
        };
    }
}
