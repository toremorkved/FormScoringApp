#if IOS
using Microsoft.Maui.Handlers;
using UIKit;

namespace NewsScoreApp.Platforms.iOS.Handlers;

/// <summary>
/// Configures the native scroll view behind every MAUI ScrollView so a drag that starts on a
/// button can still be promoted to scrolling. iOS needs a short touch delay to distinguish a
/// button tap from a pan; CanCancelContentTouches then cancels the button when the finger moves.
/// </summary>
public static class ScrollTouchHandler
{
    public static void Register()
    {
        ScrollViewHandler.Mapper.AppendToMapping("NoTouchDelay", (handler, view) =>
        {
            if (handler.PlatformView is UIScrollView scrollView)
            {
                scrollView.DelaysContentTouches = true;
                scrollView.CanCancelContentTouches = true;
            }
        });
    }
}
#endif
