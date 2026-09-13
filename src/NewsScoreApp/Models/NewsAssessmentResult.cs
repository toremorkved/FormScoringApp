namespace NewsScoreApp.Models;

/// <summary>The computed sub-score (0-3) for a single NEWS2 parameter, plus the formatted
/// value shown in the breakdown chip (e.g. "16", "86%", "C").</summary>
public sealed record ParameterScore(string Key, string ShortLabel, string DisplayValue, int? Score);

public sealed class NewsAssessmentResult
{
    public IReadOnlyList<ParameterScore> Parameters { get; init; } = Array.Empty<ParameterScore>();
    public int? TotalScore { get; init; }
    public bool HasSingleParameterScoreOfThree { get; init; }
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Incomplete;

    public string RiskTitle => RiskLevel switch
    {
        RiskLevel.Low => "LAV RISIKO",
        RiskLevel.LowMedium => "LAV-MEDIUM RISIKO",
        RiskLevel.Medium => "MEDIUM RISIKO",
        RiskLevel.High => "HØY RISIKO",
        _ => string.Empty
    };

    public string ClinicalResponse => RiskLevel switch
    {
        RiskLevel.Low => "Rutineovervåking",
        RiskLevel.LowMedium => "Økt overvåkingsfrekvens, kontakt ansvarlig sykepleier",
        RiskLevel.Medium => "Urgent vurdering av lege/sykepleier med kompetanse",
        RiskLevel.High => "Øyeblikkelig vurdering av medisinsk team (MET-team)",
        _ => "Fyll inn alle felt for å beregne score"
    };

    public string MonitoringFrequency => RiskLevel switch
    {
        RiskLevel.Low => "Min. hver 12. time",
        RiskLevel.LowMedium => "Min. hver 4.–6. time",
        RiskLevel.Medium => "Min. hver time",
        RiskLevel.High => "Kontinuerlig overvåking",
        _ => string.Empty
    };
}
