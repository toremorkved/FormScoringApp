using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.ViewModels;

/// <summary>Wraps one <see cref="ChoiceOption"/> so the dynamically generated single-choice
/// buttons can bind a Command back to their owning <see cref="FormFieldState"/> without any
/// static XAML per form - this is what makes the button grid fully recipe-driven.</summary>
public sealed partial class OptionItemViewModel : ObservableObject
{
    public ChoiceOption Option { get; }
    public FormFieldState Owner { get; }
    public ICommand SelectCommand { get; }

    [ObservableProperty] private bool isSelected;

    public OptionItemViewModel(ChoiceOption option, FormFieldState owner, ICommand selectCommand)
    {
        Option = option;
        Owner = owner;
        SelectCommand = selectCommand;
    }
}

/// <summary>A single score "chip" (e.g. the 3/2/1/0 badges next to a field) that lights up when
/// its value matches the field's current computed score.</summary>
public sealed partial class ScoreChipViewModel : ObservableObject
{
    public int Value { get; }
    [ObservableProperty] private bool isActive;
    public ScoreChipViewModel(int value) => Value = value;
}
