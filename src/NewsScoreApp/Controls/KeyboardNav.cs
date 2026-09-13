using System.Windows.Input;

namespace NewsScoreApp.Controls;

/// <summary>Attached properties that let a plain <see cref="Entry"/> expose "previous / next"
/// keyboard navigation, mirroring the accessory bar users expect from HTML forms on iOS.
/// The actual UIToolbar wiring lives in Platforms/iOS/Handlers/KeyboardToolbarHandler.cs.</summary>
public static class KeyboardNav
{
    public static readonly BindableProperty PreviousCommandProperty =
        BindableProperty.CreateAttached("PreviousCommand", typeof(ICommand), typeof(KeyboardNav), null);

    public static readonly BindableProperty NextCommandProperty =
        BindableProperty.CreateAttached("NextCommand", typeof(ICommand), typeof(KeyboardNav), null);

    public static readonly BindableProperty HasPreviousProperty =
        BindableProperty.CreateAttached("HasPrevious", typeof(bool), typeof(KeyboardNav), false);

    public static readonly BindableProperty HasNextProperty =
        BindableProperty.CreateAttached("HasNext", typeof(bool), typeof(KeyboardNav), false);

    public static ICommand? GetPreviousCommand(BindableObject view) => (ICommand?)view.GetValue(PreviousCommandProperty);
    public static void SetPreviousCommand(BindableObject view, ICommand? value) => view.SetValue(PreviousCommandProperty, value);

    public static ICommand? GetNextCommand(BindableObject view) => (ICommand?)view.GetValue(NextCommandProperty);
    public static void SetNextCommand(BindableObject view, ICommand? value) => view.SetValue(NextCommandProperty, value);

    public static bool GetHasPrevious(BindableObject view) => (bool)view.GetValue(HasPreviousProperty);
    public static void SetHasPrevious(BindableObject view, bool value) => view.SetValue(HasPreviousProperty, value);

    public static bool GetHasNext(BindableObject view) => (bool)view.GetValue(HasNextProperty);
    public static void SetHasNext(BindableObject view, bool value) => view.SetValue(HasNextProperty, value);
}
