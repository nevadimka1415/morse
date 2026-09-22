namespace MorseTrainer.Domain;

/// <summary>
/// Декодер ручной передачи: нажатия ключа превращаются в точки и тире, паузы — в границы символов и групп.
/// Длительность точки берётся из скорости (5 символов = слово PARIS, 60 зн/мин → 100 мс).
/// </summary>
public sealed class KeyerDecoder
{
    public const double DashThresholdUnits = 2;
    public const double SymbolGapUnits = 3;
    public const double GroupGapUnits = 7;

    private readonly AlphabetMode _alphabet;
    private readonly System.Text.StringBuilder _text = new();

    // Длительности для разбора качества: нажатия и паузы по их роли
    private readonly List<double> _dots = new();
    private readonly List<double> _dashes = new();
    private readonly List<double> _elementGaps = new();
    private readonly List<double> _symbolGaps = new();
    private readonly List<double> _groupGaps = new();
    private double _pendingGap = -1;

    public KeyerDecoder(AlphabetMode alphabet, int charactersPerMinute)
    {
        _alphabet = alphabet;
        UnitMilliseconds = DotMillisecondsFor(charactersPerMinute);
    }

    public double UnitMilliseconds { get; }

    /// <summary>Уже распознанный текст.</summary>
    public string Text => _text.ToString();

    /// <summary>Точки и тире символа, который ещё передаётся.</summary>
    public string PendingCode { get; private set; } = string.Empty;

    public static double DotMillisecondsFor(int charactersPerMinute) => 6000d / Math.Clamp(charactersPerMinute, 20, 300);

    /// <summary>Нажатие ключа длительностью milliseconds.</summary>
    public void Press(double milliseconds)
    {
        // Пауза перед этим нажатием закрылась: относим её к паузам внутри символа, между символами или группами
        if (_pendingGap >= 0)
        {
            if (_pendingGap >= UnitMilliseconds * GroupGapUnits)
            {
                _groupGaps.Add(_pendingGap);
            }
            else if (_pendingGap >= UnitMilliseconds * SymbolGapUnits)
            {
                _symbolGaps.Add(_pendingGap);
            }
            else
            {
                _elementGaps.Add(_pendingGap);
            }

            _pendingGap = -1;
        }

        var isDash = milliseconds >= UnitMilliseconds * DashThresholdUnits;
        (isDash ? _dashes : _dots).Add(milliseconds);
        PendingCode += isDash ? '-' : '.';
    }

    /// <summary>
    /// Пауза после отпускания ключа. Можно звать многократно с растущим значением:
    /// символ закрывается один раз после 3 точек, пробел добавляется один раз после 7.
    /// </summary>
    public void Idle(double millisecondsSinceRelease)
    {
        _pendingGap = Math.Max(_pendingGap, millisecondsSinceRelease);
        if (millisecondsSinceRelease >= UnitMilliseconds * SymbolGapUnits)
        {
            CommitSymbol();
        }

        if (millisecondsSinceRelease >= UnitMilliseconds * GroupGapUnits && _text.Length > 0 && _text[^1] != ' ')
        {
            _text.Append(' ');
        }
    }

    public void CommitSymbol()
    {
        if (PendingCode.Length == 0)
        {
            return;
        }

        _text.Append(MorseAlphabet.TryGetSymbol(PendingCode, _alphabet, out var symbol) ? symbol : '?');
        PendingCode = string.Empty;
    }

    /// <summary>Стереть последний символ (сначала незавершённый, потом из текста).</summary>
    public void Backspace()
    {
        if (PendingCode.Length > 0)
        {
            PendingCode = string.Empty;
            return;
        }

        if (_text.Length > 0)
        {
            _text.Length--;
        }
    }

    public void Clear()
    {
        _text.Clear();
        PendingCode = string.Empty;
        _dots.Clear();
        _dashes.Clear();
        _elementGaps.Clear();
        _symbolGaps.Clear();
        _groupGaps.Clear();
        _pendingGap = -1;
    }

    /// <summary>Разбор качества передачи по накопленным длительностям.</summary>
    public KeyerAnalysis Analyze() => KeyerAnalysis.From(UnitMilliseconds, _dots, _dashes, _elementGaps, _symbolGaps, _groupGaps);
}
