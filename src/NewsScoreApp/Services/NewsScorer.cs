using NewsScoreApp.Models;
using NewsScoreApp.Services.FormFormula;

namespace NewsScoreApp.Services;

/// <summary>
/// Implements the official Royal College of Physicians NEWS2 scoring tables for the
/// per-parameter scores (thresholds match the published NEWS2 parameter table 1:1), then
/// hands the total-score aggregation and risk-band decision over to DIPS's own
/// "form-description.json" calc formulas via <see cref="CalcExpressionEngine"/> — the same
/// formula strings ("NewsTotalScore", "LowRiskEvaluation", "HighRiskEvaluation" etc.) that
/// drive the real DIPS NEWS2 form. This means the risk-band rules (including the
/// "any single parameter scores 3" escalation override) come straight from DIPS, not from a
/// re-implementation, and will stay correct if DIPS ships an updated form export.
/// </summary>
public sealed class NewsScorer
{
    private const string FormDescriptionAsset = "FormDefinitions/form-description.json";

    private FormCalcRepository? _calcRepository;

    /// <summary>Loads DIPS's calc formulas from the bundled form-description.json. Must be
    /// awaited once (e.g. at app startup) before <see cref="Evaluate"/> is called.</summary>
    public async Task InitializeAsync()
    {
        _calcRepository ??= await FormCalcRepository.LoadFromRawAssetAsync(FormDescriptionAsset);
    }

    public int? ScoreRespiratoryRate(int? rr)
    {
        if (rr is not int v) return null;
        if (v <= 8) return 3;
        if (v <= 11) return 1;
        if (v <= 20) return 0;
        if (v <= 24) return 2;
        return 3; // >=25
    }

    public int? ScoreOxygenSaturation(int? spo2, SpO2Scale scale, OxygenSupplement? supplement)
    {
        if (spo2 is not int v) return null;

        if (scale == SpO2Scale.Scale1Normal)
        {
            if (v <= 91) return 3;
            if (v <= 93) return 2;
            if (v <= 95) return 1;
            return 0; // >=96
        }

        // Scale 2 (hypercapnic risk, e.g. COPD) - depends on whether patient is on air or oxygen.
        bool onOxygen = supplement == OxygenSupplement.Oxygen;
        if (!onOxygen)
        {
            if (v <= 83) return 3;
            if (v <= 85) return 2;
            if (v <= 87) return 1;
            if (v <= 92) return 0;
            if (v <= 94) return 1;
            if (v <= 96) return 2;
            return 3; // >=97 on air is an abnormally high reading -> highest risk score
        }
        else
        {
            if (v <= 83) return 3;
            if (v <= 85) return 2;
            if (v <= 87) return 1;
            if (v <= 92) return 0;
            if (v <= 94) return 1;
            if (v <= 96) return 2;
            return 3; // >=97
        }
    }

    public int? ScoreOxygenSupplement(OxygenSupplement? supplement)
    {
        if (supplement is not OxygenSupplement s) return null;
        return s == OxygenSupplement.Oxygen ? 2 : 0;
    }

    public int? ScoreSystolicBloodPressure(int? sbp)
    {
        if (sbp is not int v) return null;
        if (v <= 90) return 3;
        if (v <= 100) return 2;
        if (v <= 110) return 1;
        if (v <= 219) return 0;
        return 3; // >=220
    }

    public int? ScorePulse(int? pulse)
    {
        if (pulse is not int v) return null;
        if (v <= 40) return 3;
        if (v <= 50) return 1;
        if (v <= 90) return 0;
        if (v <= 110) return 1;
        if (v <= 130) return 2;
        return 3; // >=131
    }

    public int? ScoreConsciousness(Consciousness? consciousness)
    {
        if (consciousness is not Consciousness c) return null;
        return c == Consciousness.Alert ? 0 : 3;
    }

    public int? ScoreTemperature(double? temp)
    {
        if (temp is not double v) return null;
        if (v <= 35.0) return 3;
        if (v <= 36.0) return 1;
        if (v <= 38.0) return 0;
        if (v <= 39.0) return 1;
        return 2; // >=39.1
    }

    public static string ConsciousnessCode(Consciousness c) => c switch
    {
        Consciousness.Alert => "A",
        Consciousness.NewConfusion => "C",
        Consciousness.Voice => "V",
        Consciousness.Pain => "P",
        Consciousness.Unresponsive => "U",
        _ => "-"
    };

    public NewsAssessmentResult Evaluate(VitalSignsInput input)
    {
        var rr = ScoreRespiratoryRate(input.RespiratoryRate);
        var spo2 = ScoreOxygenSaturation(input.OxygenSaturation, input.SpO2Scale, input.OxygenSupplement);
        var o2 = ScoreOxygenSupplement(input.OxygenSupplement);
        var sbp = ScoreSystolicBloodPressure(input.SystolicBloodPressure);
        var pulse = ScorePulse(input.PulseRate);
        var acvpu = ScoreConsciousness(input.Consciousness);
        var temp = ScoreTemperature(input.Temperature);

        var parameters = new List<ParameterScore>
        {
            new("resp", "Resp", input.RespiratoryRate?.ToString() ?? "–", rr),
            new("spo2", "SpO₂", input.OxygenSaturation is int s ? $"{s}%" : "–", spo2),
            new("o2", "O₂", input.OxygenSupplement switch
            {
                Models.OxygenSupplement.None => "Luft",
                Models.OxygenSupplement.Oxygen => "Tilsk.",
                _ => "–"
            }, o2),
            new("bt", "BT", input.SystolicBloodPressure?.ToString() ?? "–", sbp),
            new("puls", "Puls", input.PulseRate?.ToString() ?? "–", pulse),
            new("acvpu", "ACVPU", input.Consciousness is Consciousness c ? ConsciousnessCode(c) : "–", acvpu),
            new("temp", "Temp", input.Temperature is double t ? $"{t:0.0}°" : "–", temp),
        };

        if (!input.IsComplete)
        {
            return new NewsAssessmentResult
            {
                Parameters = parameters,
                TotalScore = null,
                RiskLevel = RiskLevel.Incomplete
            };
        }

        // Build the variable context using DIPS's exact calcId names, then let DIPS's own
        // calc formulas (from form-description.json) compute the total and risk band —
        // NEWS2SatO2Scale2 is left null here since this app only ever uses SpO2 Scale 1.
        var context = new Dictionary<string, object?>
        {
            ["NEWS2Resp"] = (double)rr!.Value,
            ["NEWS2SatO2Scale1"] = (double)spo2!.Value,
            ["NEWS2SatO2Scale2"] = null,
            ["NEWS2Oxygen"] = (double)o2!.Value,
            ["NEWS2SysBt"] = (double)sbp!.Value,
            ["NEWS2SPulse"] = (double)pulse!.Value,
            ["NEWS2SConciussnes"] = (double)acvpu!.Value,
            ["NEWS2STemp"] = (double)temp!.Value,
        };

        var formulas = _calcRepository?.Formulas
            ?? throw new InvalidOperationException($"{nameof(NewsScorer)}.{nameof(InitializeAsync)} must be awaited before Evaluate().");

        double total = ToDouble(CalcExpressionEngine.Evaluate(formulas["NewsTotalScore"], context));
        context["NewsTotalScore"] = total;

        bool low = ToBool(CalcExpressionEngine.Evaluate(formulas["LowRiskEvaluation"], context));
        bool low2 = ToBool(CalcExpressionEngine.Evaluate(formulas["LowRisk2Evaluation"], context));
        bool lowMedium = ToBool(CalcExpressionEngine.Evaluate(formulas["LowMediumRiskEvaluation"], context));
        bool medium = ToBool(CalcExpressionEngine.Evaluate(formulas["MediumRiskEvaluation"], context));
        bool high = ToBool(CalcExpressionEngine.Evaluate(formulas["HighRiskEvaluation"], context));

        RiskLevel risk = high ? RiskLevel.High
            : medium ? RiskLevel.Medium
            : lowMedium ? RiskLevel.LowMedium
            : (low || low2) ? RiskLevel.Low
            : RiskLevel.Low;

        bool anyThree = new[] { rr.Value, spo2.Value, o2.Value, sbp.Value, pulse.Value, acvpu.Value, temp.Value }.Any(x => x == 3);

        return new NewsAssessmentResult
        {
            Parameters = parameters,
            TotalScore = (int)total,
            HasSingleParameterScoreOfThree = anyThree,
            RiskLevel = risk
        };
    }

    private static double ToDouble(object? v) => v switch
    {
        double d => d,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d2) => d2,
        _ => 0
    };

    private static bool ToBool(object? v) => v switch
    {
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
