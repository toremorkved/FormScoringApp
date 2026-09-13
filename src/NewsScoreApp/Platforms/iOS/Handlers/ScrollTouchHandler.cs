#if IOS
using Microsoft.Maui.Handlers;
using UIKit;

namespace NewsScoreApp.Platforms.iOS.Handlers;

/// <summary>
/// Fixes the classic "have to tap twice" MAUI-on-iOS issue: a page's <see cref="UIScrollView"/>
/// (which backs every MAUI ScrollView) defaults to <c>DelaysContentTouches = true</c>, so while a
/// text field elsewhere on the page has keyboard focus, the first tap on a button is consumed by
/// the scroll view just to check whether it's the start of a scroll gesture/should dismiss the
/// keyboard, and only a second tap actually reaches the button. Turning this off makes buttons
/// react to the very first tap, matching how native iOS apps behave.
/// </summary>
public static class ScrollTouchHandler
{
    public static void Register()
    {
        ScrollViewHandler.Mapper.AppendToMapping("NoTouchDelay", (handler, view) =>
        {
            if (handler.PlatformView is UIScrollView scrollView)
                scrollView.DelaysContentTouches = false;
        });
    }
}
#endif
