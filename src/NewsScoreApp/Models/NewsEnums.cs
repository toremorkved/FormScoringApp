namespace NewsScoreApp.Models;

/// <summary>Which of the two NEWS2 SpO2 scales is in use. Scale 2 is for patients with a
/// hypercapnic drive (e.g. COPD) where a lower target saturation is normal.</summary>
public enum SpO2Scale
{
    Scale1Normal,
    Scale2HypercapnicRisk
}

public enum OxygenSupplement
{
    None,   // Romluft
    Oxygen  // Oksygen
}

public enum Consciousness
{
    Alert,          // Alert
    NewConfusion,   // Ny forvirring (counts as "C" in ACVPU but scores like V/P/U = 3)
    Voice,          // Reagerer på stemme
    Pain,           // Reagerer på smerte
    Unresponsive    // Ikke-responsiv
}

public enum RiskLevel
{
    Incomplete,
    Low,
    LowMedium,
    Medium,
    High
}
