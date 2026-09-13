#if IOS
using System.Linq;
using UIKit;

namespace NewsScoreApp.Platforms.iOS.Handlers;

/// <summary>
/// Configures the currently-presented modal UIViewController as an iOS "half sheet" (the same
/// UISheetPresentationController-based card Apple's own apps use for quick settings/options
/// screens) - grabber handle, medium detent by default, swipe-to-dismiss, with a large detent
/// still reachable by dragging up.
/// </summary>
public static class SheetPresentationHelper
{
    /// <summary>Must be called on the page BEFORE Navigation.PushModalAsync, since
    /// ModalPresentationStyle only takes effect if set before the view controller is actually
    /// presented (MAUI's default here is FullScreen, which has no SheetPresentationController
    /// at all, so the half-sheet detents would silently no-op without this).</summary>
    public static void PrepareHalfSheet(Page page)
    {
        page.HandlerChanged += (_, _) =>
        {
            if (page.Handler?.PlatformView is UIViewController controller)
            {
                controller.ModalPresentationStyle = UIModalPresentationStyle.PageSheet;
            }
        };
    }

    /// <summary>Call right after Navigation.PushModalAsync completes, once the view controller
    /// is actually on screen, to configure its detents/grabber.</summary>
    public static void ApplyHalfSheetDetents()
    {
        var keyWindow = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        var presented = keyWindow?.RootViewController?.PresentedViewController;
        if (presented?.SheetPresentationController is not { } sheet) return;

        sheet.Detents = new[]
        {
            UISheetPresentationControllerDetent.CreateMediumDetent(),
            UISheetPresentationControllerDetent.CreateLargeDetent()
        };
        sheet.PrefersGrabberVisible = true;
        sheet.PreferredCornerRadius = 20;
    }
}
#endif


