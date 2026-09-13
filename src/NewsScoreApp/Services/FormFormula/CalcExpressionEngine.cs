using System.Globalization;

namespace NewsScoreApp.Services.FormFormula;

/// <summary>
/// Interpreter for the small formula DSL DIPS embeds in openEHR form definitions
/// (viewConfig.annotations.calc). Supports the subset actually used by the NEWS2
/// form export: IF, IFNULL/ifnull, AND/and, OR/or, arithmetic +, comparisons
/// (=, &lt;, &gt;, &lt;=, &gt;=), string/number literals and $variable / bare
/// variable references resolved from a supplied context dictionary.
///
/// This lets the app consume DIPS's real "form-description.json" export
/// unmodified: the same calc strings that drive the desktop/DIPS form are
/// evaluated here to produce the NEWS2 total score and risk-band booleans.
/// </summary>
public sealed class CalcExpressionEngine
{
    private readonly IReadOnlyDictionary<string, object?> _context;
    private readonly string _text;
    private int _pos;

    private CalcExpressionEngine(string text, IReadOnlyDictionary<string, object?> context)
    {
        _text = text;
        _context = context;
    }

    /// <summary>Evaluates a calc formula string against a variable context (calcId -> value).</summary>
    public static object? Evaluate(string formula, IReadOnlyDictionary<string, object?> context)
    {
        var engine = new CalcExpressionEngine(formula, context);
        var result = engine.ParseExpression();
        engine.SkipWhitespace();
        return result;
    }

    // ----- Tokenizer helpers -----

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
    }

    private bool TryMatch(string literal)
    {
        SkipWhitespace();
        if (_pos + literal.Length > _text.Length) return false;
        if (string.Compare(_text, _pos, literal, 0, literal.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        _pos += literal.Length;
        return true;
    }

    private char Peek()
    {
        SkipWhitespace();
        return _pos < _text.Length ? _text[_pos] : '\0';
    }

    private void Expect(char c)
    {
        SkipWhitespace();
        if (_pos >= _text.Length || _text[_pos] != c)
            throw new FormatException($"Expected '{c}' at position {_pos} in: {_text}");
        _pos++;
    }

    // ----- Grammar -----
    // expression := comparison
    // comparison := term (('=' | '<' | '>' | '<=' | '>=') term)?
    // term        := factor ('+' factor)*
    // factor      := call | literal | variable | '(' expression ')'
    // call        := name '(' args ')'

    private object? ParseExpression() => ParseComparison();

    private object? ParseComparison()
    {
        var left = ParseTerm();
        SkipWhitespace();
        string? op = null;
        if (TryMatch("<=")) op = "<=";
        else if (TryMatch(">=")) op = ">=";
        else if (TryMatch("=")) op = "=";
        else if (TryMatch("<")) op = "<";
        else if (TryMatch(">")) op = ">";

        if (op == null) return left;

        var right = ParseTerm();
        return Compare(left, op, right);
    }

    private object? ParseTerm()
    {
        var value = ParseFactor();
        SkipWhitespace();
        while (Peek() == '+')
        {
            _pos++;
            var rhs = ParseFactor();
            value = ToDouble(value) + ToDouble(rhs);
        }
        return value;
    }

    private object? ParseFactor()
    {
        SkipWhitespace();
        char c = Peek();

        if (c == '(')
        {
            _pos++;
            var v = ParseExpression();
            Expect(')');
            return v;
        }

        if (c == '"')
        {
            return ParseStringLiteral();
        }

        if (c == '$' || char.IsLetter(c) || c == '_')
        {
            return ParseIdentifierOrCall();
        }

        if (char.IsDigit(c) || c == '-' || c == '.')
        {
            return ParseNumberLiteral();
        }

        throw new FormatException($"Unexpected character '{c}' at position {_pos} in: {_text}");
    }

    private string ParseStringLiteral()
    {
        Expect('"');
        int start = _pos;
        while (_pos < _text.Length && _text[_pos] != '"') _pos++;
        string s = _text[start.._pos];
        Expect('"');
        return s;
    }

    private object? ParseNumberLiteral()
    {
        int start = _pos;
        if (Peek() == '-') _pos++;
        while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
        var s = _text[start.._pos];
        return double.Parse(s, CultureInfo.InvariantCulture);
    }

    private object? ParseIdentifierOrCall()
    {
        bool hasDollar = Peek() == '$';
        if (hasDollar) _pos++;

        int start = _pos;
        while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
        string name = _text[start.._pos];

        // Optional aqlPath-style suffix like "/numerator" — treated as part of variable name.
        while (_pos < _text.Length && _text[_pos] == '/')
        {
            int slashStart = _pos;
            _pos++;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
            name += _text[slashStart.._pos];
        }

        SkipWhitespace();
        if (Peek() == '(')
        {
            _pos++;
            var args = new List<object?>();
            SkipWhitespace();
            if (Peek() != ')')
            {
                args.Add(ParseExpression());
                SkipWhitespace();
                while (Peek() == ',')
                {
                    _pos++;
                    args.Add(ParseExpression());
                    SkipWhitespace();
                }
            }
            Expect(')');
            return CallFunction(name, args);
        }

        return ResolveVariable(name);
    }

    private object? ResolveVariable(string name)
    {
        if (_context.TryGetValue(name, out var val)) return val;
        return null;
    }

    private object? CallFunction(string name, List<object?> args)
    {
        switch (name.ToLowerInvariant())
        {
            case "if":
                bool cond = ToBool(args[0]);
                if (cond) return args.Count > 1 ? args[1] : null;
                return args.Count > 2 ? args[2] : null;

            case "ifnull":
                var v = args[0];
                return v ?? (args.Count > 1 ? args[1] : null);

            case "and":
                return args.All(ToBool);

            case "or":
                return args.Any(ToBool);

            case "nowticks":
                return (double)DateTime.UtcNow.Ticks;

            default:
                // Unknown function - fail loudly so gaps in the DSL surface are caught during testing.
                throw new NotSupportedException($"Unsupported calc function '{name}'");
        }
    }

    // ----- Value coercion -----

    private static bool ToBool(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
        double d => d != 0,
        _ => false
    };

    private static double ToDouble(object? v) => v switch
    {
        null => 0,
        double d => d,
        int i => i,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2) => d2,
        bool b => b ? 1 : 0,
        _ => 0
    };

    private static object Compare(object? left, string op, object? right)
    {
        // String equality (used for coded-text comparisons like "01"=$SelectedSpO2Scale).
        if (left is string || right is string)
        {
            if (op == "=") return string.Equals(Str(left), Str(right), StringComparison.Ordinal);
            // Fall through to numeric compare for < > etc. against numeric-looking strings.
        }

        double l = ToDouble(left);
        double r = ToDouble(right);
        return op switch
        {
            "=" => l == r,
            "<" => l < r,
            ">" => l > r,
            "<=" => l <= r,
            ">=" => l >= r,
            _ => throw new NotSupportedException($"Unknown operator '{op}'")
        };
    }

    private static string Str(object? v) => v switch
    {
        null => "",
        double d => d.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => v.ToString() ?? ""
    };
}
