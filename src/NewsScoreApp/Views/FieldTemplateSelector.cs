using NewsScoreApp.ViewModels;

namespace NewsScoreApp.Views;

/// <summary>Chooses between the numeric-entry template and the single-choice template for a
/// given <see cref="GenericFieldViewModel"/> - this is the mechanism that lets one generic
/// BindableLayout render completely different field types purely from recipe data.</summary>
public sealed class FieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NumericTemplate { get; set; }
    public DataTemplate? ChoiceTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        var field = (GenericFieldViewModel)item;
        if (field.IsNumeric) return NumericTemplate!;
        if (field.IsText) return TextTemplate!;
        return ChoiceTemplate!;
    }
}
