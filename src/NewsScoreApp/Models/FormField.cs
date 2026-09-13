namespace NewsScoreApp.Models;

/// <summary>Identifies each focusable step in the registration form, used to drive
/// auto-advance, the keyboard prev/next toolbar and auto-scroll-into-view.</summary>
public enum FormField
{
    RespiratoryRate,
    OxygenSaturation,
    OxygenSupplement,
    SystolicBloodPressure,
    PulseRate,
    Consciousness,
    Temperature,
    None
}
