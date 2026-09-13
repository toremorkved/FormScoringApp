using NewsScoreApp.Models.FormEngine;

namespace NewsScoreApp.Services.FormEngine;

/// <summary>
/// Generic scorer that works for any <see cref="FormRecipe"/>: scores each field against its
/// declared <see cref="ScoreBand"/>s (numeric) or <see cref="ChoiceOption"/> (single-choice), sums
/// them, then resolves the risk band - including the "any single parameter at max score escalates
/// the band" override rule that NEWS2 (and several other early-warning scores) use. This is a
/// declarative re-expression of the same rule DIPS encodes as calc-formulas in form-description.json,
/// but written as plain data so it applies to any future recipe without new C# code.
/// </summary>
public sealed class GenericFormScorer
{
    public int? ScoreNumericField(FormSection field, string text)
    {
        var normalized = text.Replace(',', '.');
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return null;

        foreach (var band in field.ScoreBands)
        {
            bool aboveMin = band.MinValue is null || value >= band.MinValue;
            bool belowMax = band.MaxValue is null || value <= band.MaxValue;
            if (aboveMin && belowMax) return band.Score;
        }

        return null;
    }

    public int? ScoreChoiceField(FormSection field, string? selectedValue)
    {
        if (selectedValue is null) return null;
        var option = field.Options.FirstOrDefault(o => o.Value == selectedValue);
        return option?.Score;
    }

    /// <summary>Validates a raw numeric string against the field's min/max range (used to drive
    /// auto-advance - a field only triggers advance once its value is physiologically valid).</summary>
    public bool IsNumericValueValid(FormSection field, string text)
    {
        var normalized = text.Replace(',', '.');
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return false;

        if (field.Min is double min && value < min) return false;
        if (field.Max is double max && value > max) return false;
        return true;
    }

    public FormAssessmentResult Evaluate(FormRecipe recipe, IReadOnlyList<FormFieldState> fields)
    {
        bool allAnswered = fields.All(f => f.HasValue);
        if (!allAnswered)
        {
            return new FormAssessmentResult { IsComplete = false, TotalScore = null, RiskBand = null };
        }

        // Document-style recipes (e.g. a structured epikrise) have no total/risk band concept at
        // all - "complete" just means every required field has been filled in.
        if (!recipe.IsScored)
        {
            return new FormAssessmentResult { IsComplete = true, TotalScore = null, RiskBand = null };
        }

        var scores = fields.Select(f => f.Score ?? 0).ToList();
        int total = scores.Sum();

        var scoring = recipe.Scoring;
        var band = scoring.RiskBands
            .FirstOrDefault(b => total >= b.MinTotal && (b.MaxTotal is null || total <= b.MaxTotal));

        if (scoring.EscalateOnAnyMaxScore && scores.Any(s => s == scoring.MaxScoreValue) && scoring.EscalatedBandId != null)
        {
            var escalated = scoring.RiskBands.FirstOrDefault(b => b.Id == scoring.EscalatedBandId);
            // Only escalate if the escalated band ranks at or above the naturally computed band
            // (mirrors NEWS2: a 3-in-one-parameter never *downgrades* an already-higher band).
            if (escalated != null && (band == null || RankOf(scoring, escalated) > RankOf(scoring, band)))
                band = escalated;
        }

        return new FormAssessmentResult { IsComplete = true, TotalScore = total, RiskBand = band };
    }

    private static int RankOf(ScoringDefinition scoring, RiskBand band) =>
        scoring.RiskBands.FindIndex(b => b.Id == band.Id);
}
