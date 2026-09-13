using Microsoft.Maui.Graphics;
using NewsScoreApp.Models.History;

namespace NewsScoreApp.Views;

/// <summary>
/// Renders the score-trend line chart via <see cref="Microsoft.Maui.Graphics"/> /
/// <see cref="GraphicsView"/> - deliberately hand-drawn rather than pulling in a charting NuGet
/// package: the need here is a single simple line/dot plot of total score over time, which a
/// ~100-line IDrawable covers without a new dependency (and its associated risk of native-build
/// breakage, which this app has already fought hard with CrossIntelligence/Newtonsoft.Json.Schema).
/// Only the recipe's <see cref="Models.FormEngine.FormAssessmentResult.TotalScore"/> is charted
/// (not individual field sub-scores) - that's the one number that's directly comparable across
/// every recipe and is what the risk band/escalation decision is actually based on, so it's the
/// single most clinically meaningful trend line to show.
/// </summary>
public sealed class ScoreTrendDrawable : IDrawable
{
    private const float LeftAxisWidth = 28f;
    private const float BottomAxisHeight = 22f;
    private const float TopPadding = 16f;
    private const float RightPadding = 12f;
    private const float PointRadius = 4f;

    public List<AssessmentHistoryEntry> Entries { get; set; } = new();
    public Color AxisColor { get; set; } = Colors.Gray;
    public Color LineColor { get; set; } = Color.FromArgb("#3B82F6");
    public Color LabelColor { get; set; } = Colors.Gray;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Entries.Count == 0) return;

        var plotLeft = dirtyRect.Left + LeftAxisWidth;
        var plotRight = dirtyRect.Right - RightPadding;
        var plotTop = dirtyRect.Top + TopPadding;
        var plotBottom = dirtyRect.Bottom - BottomAxisHeight;
        var plotWidth = Math.Max(1f, plotRight - plotLeft);
        var plotHeight = Math.Max(1f, plotBottom - plotTop);

        var maxScore = Math.Max(3, Entries.Max(e => e.TotalScore)); // never let the axis go below 3 - keeps a single low reading from looking dramatically "full scale"
        var minTime = Entries.Min(e => e.Timestamp).Ticks;
        var maxTime = Entries.Max(e => e.Timestamp).Ticks;
        var timeSpan = Math.Max(1, maxTime - minTime);

        // Y-axis gridlines + score labels (0, mid, max) - just enough to read the chart without
        // it turning into a cluttered ruler.
        canvas.StrokeColor = AxisColor.WithAlpha(0.3f);
        canvas.StrokeSize = 1;
        canvas.FontColor = LabelColor;
        canvas.FontSize = 11;
        foreach (var fraction in new[] { 0.0, 0.5, 1.0 })
        {
            var y = plotBottom - (float)(fraction * plotHeight);
            canvas.DrawLine(plotLeft, y, plotRight, y);
            var label = ((int)Math.Round(fraction * maxScore)).ToString();
            canvas.DrawString(label, dirtyRect.Left, y - 7, LeftAxisWidth - 4, 14,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // Map each entry to a plot-space point, single pass over entries reused for both the
        // line and the point markers below.
        var points = new PointF[Entries.Count];
        for (int i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            var xFraction = Entries.Count == 1 ? 0.5f : (float)(entry.Timestamp.Ticks - minTime) / timeSpan;
            var yFraction = entry.TotalScore / (float)maxScore;
            var x = plotLeft + xFraction * plotWidth;
            var y = plotBottom - yFraction * plotHeight;
            points[i] = new PointF(x, y);
        }

        // Connecting line - only meaningful with 2+ points.
        if (points.Length > 1)
        {
            canvas.StrokeColor = LineColor;
            canvas.StrokeSize = 2.5f;
            var path = new PathF();
            path.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                path.LineTo(points[i]);
            canvas.DrawPath(path);
        }

        // Point markers, colored per-entry by that assessment's own risk-band color - so a
        // glance at the chart shows not just the score trend but which points were already
        // flagged as elevated risk at the time, without needing a separate legend.
        for (int i = 0; i < points.Length; i++)
        {
            var color = Color.FromArgb(string.IsNullOrEmpty(Entries[i].RiskBandColor) ? "#3B82F6" : Entries[i].RiskBandColor);
            canvas.FillColor = color;
            canvas.FillCircle(points[i], PointRadius);
        }

        // X-axis: first and last timestamp only, to stay readable regardless of how many points
        // are plotted (a label per point would overlap badly with more than a handful of entries).
        canvas.FontColor = LabelColor;
        canvas.FontSize = 11;
        canvas.DrawString(FormatAxisDate(Entries[0].Timestamp), plotLeft, plotBottom + 4, 100, BottomAxisHeight - 4,
            HorizontalAlignment.Left, VerticalAlignment.Top);
        if (Entries.Count > 1)
        {
            canvas.DrawString(FormatAxisDate(Entries[^1].Timestamp), plotRight - 100, plotBottom + 4, 100, BottomAxisHeight - 4,
                HorizontalAlignment.Right, VerticalAlignment.Top);
        }
    }

    private static string FormatAxisDate(DateTime dt) => dt.ToString("dd.MM HH:mm");
}
