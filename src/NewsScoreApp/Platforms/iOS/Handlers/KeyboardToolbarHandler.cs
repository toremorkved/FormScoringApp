#if IOS
using CoreGraphics;
using Microsoft.Maui.Handlers;
using NewsScoreApp.Controls;
using UIKit;

namespace NewsScoreApp.Platforms.iOS.Handlers;

/// <summary>
/// Adds a native UIToolbar (the classic "&lt; &gt; Done" input accessory bar) above the
/// keyboard for every Entry that opts in via <see cref="KeyboardNav"/> attached properties.
/// This is the same mechanism Safari/Mail use for "Previous/Next field" navigation, so it
/// feels completely native instead of a custom MAUI overlay.
/// </summary>
public static class KeyboardToolbarHandler
{
    public static void Register()
    {
        EntryHandler.Mapper.AppendToMapping("KeyboardToolbar", (handler, view) =>
        {
            ApplyToolbar(handler.PlatformView, view, () => handler.PlatformView.ResignFirstResponder());
        });

        // Same accessory bar for multi-line free-text fields (Editor/UITextView) - e.g. the
        // narrative sections of a structured epikrise - so Prev/Next/Done behave identically
        // to the numeric Entry fields instead of leaving text fields without keyboard navigation.
        EditorHandler.Mapper.AppendToMapping("KeyboardToolbar", (handler, view) =>
        {
            ApplyToolbar(handler.PlatformView, view, () => handler.PlatformView.ResignFirstResponder());
        });
    }

    private static void ApplyToolbar(UIView platformView, IView view, Action resignFirstResponder)
    {
        if (view is not BindableObject bindable)
            return;

        bool hasPrev = KeyboardNav.GetHasPrevious(bindable);
        bool hasNext = KeyboardNav.GetHasNext(bindable);

        if (!hasPrev && !hasNext)
            return;

        var toolbar = new UIToolbar(new CGRect(0, 0, UIScreen.MainScreen.Bounds.Width, 44))
        {
            BarStyle = UIBarStyle.Default,
            Translucent = true
        };

        var prevButton = new UIBarButtonItem(UIImage.GetSystemImage("chevron.up")!, UIBarButtonItemStyle.Plain,
            (s, e) => KeyboardNav.GetPreviousCommand(bindable)?.Execute(null))
        {
            Enabled = hasPrev
        };
        var nextButton = new UIBarButtonItem(UIImage.GetSystemImage("chevron.down")!, UIBarButtonItemStyle.Plain,
            (s, e) => KeyboardNav.GetNextCommand(bindable)?.Execute(null))
        {
            Enabled = hasNext
        };
        var flex = new UIBarButtonItem(UIBarButtonSystemItem.FlexibleSpace);
        var done = new UIBarButtonItem(UIBarButtonSystemItem.Done, (s, e) => resignFirstResponder());

        toolbar.SetItems(new[] { prevButton, nextButton, flex, done }, false);

        switch (platformView)
        {
            case UITextField textField: textField.InputAccessoryView = toolbar; break;
            case UITextView textView: textView.InputAccessoryView = toolbar; break;
        }
    }
}
#endif
