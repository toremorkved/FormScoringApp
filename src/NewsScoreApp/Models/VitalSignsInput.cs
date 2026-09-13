namespace NewsScoreApp.Models;

/// <summary>Raw vital-sign inputs captured from the registration form. Nullable value types
/// let us distinguish "not yet filled in" from a real clinical value of zero.</summary>
public sealed class VitalSignsInput
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public int? RespiratoryRate { get; set; }
    public int? OxygenSaturation { get; set; }
    public SpO2Scale SpO2Scale { get; set; } = SpO2Scale.Scale1Normal;
    public OxygenSupplement? OxygenSupplement { get; set; }
    public int? SystolicBloodPressure { get; set; }
    public int? PulseRate { get; set; }
    public Consciousness? Consciousness { get; set; }
    public double? Temperature { get; set; }

    public bool IsComplete =>
        RespiratoryRate.HasValue &&
        OxygenSaturation.HasValue &&
        OxygenSupplement.HasValue &&
        SystolicBloodPressure.HasValue &&
        PulseRate.HasValue &&
        Consciousness.HasValue &&
        Temperature.HasValue;

    public int MissingFieldCount()
    {
        int missing = 0;
        if (!RespiratoryRate.HasValue) missing++;
        if (!OxygenSaturation.HasValue) missing++;
        if (!OxygenSupplement.HasValue) missing++;
        if (!SystolicBloodPressure.HasValue) missing++;
        if (!PulseRate.HasValue) missing++;
        if (!Consciousness.HasValue) missing++;
        if (!Temperature.HasValue) missing++;
        return missing;
    }
}
