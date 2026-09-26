using System.Text;

namespace MorseTrainer.Domain;

/// <summary>
/// Декодер морзянки по звуку: микрофон у динамика рации, приёмника, генератора настоящего ключа — или запись.
/// Тон ищется сам (300–1200 Гц): новым тоном считается пик, который выше соседних частот и держится три поиска подряд, —
/// всплеск шума тоном не станет; при дрейфе тон подстраивается только по звучащему знаку. Уровни сигнала и шума
/// подстраиваются (замирания), а звук засчитывается, только если он заметно громче шума на соседних частотах, —
/// в тихой комнате или при гуле сети декодер молчит, а не печатает мусор.
/// Длина точки берётся из самих нажатий — скорость заранее не нужна, ручная передача с неровностями разбирается.
/// Первые знаки, пока длина точки неизвестна, копятся и расшифровываются задним числом; когда тон найден, последние
/// 400 мс звука проходятся заново — первый звук не обрезается (его начало пришлось на поиск тона).
/// Длительности копятся для «Разбора» (KeyerAnalysis) — так проверяется передача на настоящем ключе.
/// Потокобезопасен: звук подаётся из потока записи, текст читается из интерфейса.
/// </summary>
public sealed class MorseAudioDecoder
{
    public const int MinToneHz = 300;
    public const int MaxToneHz = 1200;

    // Шаг анализа 5 мс: у точки на 150 зн/мин (40 мс) — 8 шагов
    private const double HopSeconds = 0.005;
    private const double HopMs = HopSeconds * 1000;
    // Окно тонового фильтра 20 мс: полоса ~50 Гц — шум мимо тона почти не проходит, фронты размываются симметрично
    private const double WindowSeconds = 0.02;
    // Поиск тона: окно 60 мс (разрешение ~17 Гц); весь диапазон — пока тон не найден, рядом с ним — для дрейфа
    private const double SearchWindowSeconds = 0.06;
    // Сколько звука хранится: найдя тон, декодер проходит их заново (тон подтверждается за ~150 мс)
    private const double HistorySeconds = 0.4;
    // Новый тон — пик, найденный столько поисков подряд (раз в 50 мс) на той же частоте
    private const int ToneConfirmations = 3;
    // Пик тона при поиске: во столько раз выше медианы диапазона и среднего соседних частот (±100 и ±150 Гц)
    private const double PeakOverMedian = 4;
    private const double PeakOverNeighbours = 3;
    // Шум рядом с тоном (частоты ±150 и ±250 Гц): сигнал должен быть выше него во столько раз, а начало звука — во столько
    private const double SignalOverFloor = 5;
    private const double MarkOverFloor = 3.5;
    // Пороги в точках: тире длиннее 2 точек; символ закрывается паузой от 2 точек; группа — от 4,6 (или по Фарнсворту)
    private const double DashUnits = 2;
    private const double SymbolGapUnits = 2;
    private const double MinGroupGapUnits = 4.6;
    private const int RecentCount = 24;

    private readonly object _sync = new();
    private readonly int _sampleRate;
    private readonly AlphabetMode _alphabet;
    private readonly int _hop;
    private readonly int _window;
    private readonly float[] _history;
    private readonly int _searchLength;
    private readonly float[] _hann;
    private readonly float[] _windowHann;
    private int _historyPos;
    private long _samplesSeen;
    private int _hopFill;
    private int _hopsSinceSearch;

    private double _toneHz;
    private double _candidateHz;
    private int _candidateCount;
    private int _marksSinceTone;
    private double _signal;
    private double _noise;
    private double _floor;
    private double _gapBeforeMark;
    private bool _levelsReady;
    private double _level;
    private bool _keyDown;
    private int _candidateHops;
    private double _stateMs;

    private double _dotMs;
    private int _marksSinceAnchor;
    private readonly List<double> _recentMarks = new();
    private readonly List<double> _recentLongGaps = new();
    private readonly List<(bool IsMark, double Ms)> _undecided = new();
    private readonly StringBuilder _code = new();
    private readonly StringBuilder _text = new();
    private bool _spaceAdded = true;

    private readonly List<double> _dots = new();
    private readonly List<double> _dashes = new();
    private readonly List<double> _elementGaps = new();
    private readonly List<double> _symbolGaps = new();
    private readonly List<double> _groupGaps = new();

    public MorseAudioDecoder(int sampleRate, AlphabetMode alphabet)
    {
        if (sampleRate is < 4000 or > 192_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _sampleRate = sampleRate;
        _alphabet = alphabet;
        _hop = Math.Max(1, (int)Math.Round(sampleRate * HopSeconds));
        _window = Math.Max(_hop, (int)Math.Round(sampleRate * WindowSeconds));
        _history = new float[(int)Math.Round(sampleRate * HistorySeconds)];
        _searchLength = (int)Math.Round(sampleRate * SearchWindowSeconds);
        // Окно Ханна для поиска тона считается один раз: боковые лепестки ниже — пик тона чётче на фоне шума
        _hann = new float[_searchLength];
        for (var index = 0; index < _hann.Length; index++)
        {
            _hann[index] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (_hann.Length - 1)));
        }

        // То же для шума рядом с тоном (окно 20 мс): утечка самого тона на соседние частоты почти нулевая
        _windowHann = new float[_window];
        for (var index = 0; index < _windowHann.Length; index++)
        {
            _windowHann[index] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (_windowHann.Length - 1)));
        }
    }

    /// <summary>Распознанный текст.</summary>
    public string Text
    {
        get
        {
            lock (_sync)
            {
                return _text.ToString();
            }
        }
    }

    /// <summary>Точки и тире знака, который ещё звучит.</summary>
    public string PendingCode
    {
        get
        {
            lock (_sync)
            {
                return _code.ToString();
            }
        }
    }

    /// <summary>Найденный тон в герцах; 0 — тон ещё не найден.</summary>
    public int ToneHz
    {
        get
        {
            lock (_sync)
            {
                return (int)Math.Round(_toneHz);
            }
        }
    }

    /// <summary>Скорость по длине точки (знаков в минуту); 0 — ещё не определена.</summary>
    public int CharactersPerMinute
    {
        get
        {
            lock (_sync)
            {
                return _dotMs > 0 ? (int)Math.Round(6000 / _dotMs) : 0;
            }
        }
    }

    /// <summary>Уровень тона 0…1 для индикатора (относительно найденного сигнала).</summary>
    public double Level
    {
        get
        {
            lock (_sync)
            {
                return _level;
            }
        }
    }

    /// <summary>Звучит ли тон сейчас (ключ нажат).</summary>
    public bool KeyDown
    {
        get
        {
            lock (_sync)
            {
                return _keyDown;
            }
        }
    }

    /// <summary>Отсчёты 16 бит (запись с микрофона).</summary>
    public void Process(ReadOnlySpan<short> samples)
    {
        lock (_sync)
        {
            foreach (var sample in samples)
            {
                Add(sample / 32768f);
            }
        }
    }

    /// <summary>Отсчёты −1…1.</summary>
    public void Process(ReadOnlySpan<float> samples)
    {
        lock (_sync)
        {
            foreach (var sample in samples)
            {
                Add(sample);
            }
        }
    }

    /// <summary>Конец записи: последний знак закрывается, даже если после него нет паузы.</summary>
    public void Flush()
    {
        lock (_sync)
        {
            if (_keyDown)
            {
                _keyDown = false;
                EndMark(_stateMs);
                _stateMs = 0;
            }

            if (_dotMs <= 0 && _undecided.Count > 0)
            {
                LearnDot(force: true);
            }

            CommitSymbol();
        }
    }

    /// <summary>Очистить текст и разбор (тон и скорость остаются — та же станция).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _text.Clear();
            _code.Clear();
            _undecided.Clear();
            _spaceAdded = true;
            _dots.Clear();
            _dashes.Clear();
            _elementGaps.Clear();
            _symbolGaps.Clear();
            _groupGaps.Clear();
        }
    }

    /// <summary>Разбор ручной передачи по принятым длительностям — как у экранного ключа.</summary>
    public KeyerAnalysis Analyze()
    {
        lock (_sync)
        {
            return KeyerAnalysis.From(_dotMs > 0 ? _dotMs : 100, _dots.ToArray(), _dashes.ToArray(), _elementGaps.ToArray(),
                _symbolGaps.ToArray(), _groupGaps.ToArray());
        }
    }

    private void Add(float sample)
    {
        _history[_historyPos] = sample;
        _historyPos = (_historyPos + 1) % _history.Length;
        _samplesSeen++;
        if (++_hopFill >= _hop)
        {
            _hopFill = 0;
            Hop();
        }
    }

    private void Hop()
    {
        // Тон: пока не найден или ещё не дал ни одного знака (мог оказаться помехой) — ищем часто по всему диапазону;
        // после долгой тишины — тоже (могла смениться станция). Дрейф — только по звучащему знаку: в паузе рядом с тоном
        // один шум, и тон «уползал» бы за ним
        var wide = _toneHz <= 0 || _marksSinceTone == 0 || (!_keyDown && _stateMs > 3000);
        _hopsSinceSearch++;
        if (_samplesSeen >= _searchLength && (wide ? _hopsSinceSearch >= 10 : _keyDown && _stateMs >= 50 && _hopsSinceSearch >= 20))
        {
            _hopsSinceSearch = 0;
            if (SearchTone(wide))
            {
                Rewind();
            }
        }

        if (_toneHz > 0)
        {
            TrackFloor(Floor(0));
            Step(Magnitude(_toneHz, _window, null));
        }
        else
        {
            Step(0);
        }
    }

    /// <summary>
    /// Новый тон найден: последние 300 мс проходятся заново с ним — звук, который уже шёл, пока тон искался, получает
    /// своё настоящее начало. Тишина до этих 300 мс сохраняется (пауза перед первым звуком не укорачивается).
    /// </summary>
    private void Rewind()
    {
        var available = (int)Math.Min(_samplesSeen, _history.Length);
        var replayHops = Math.Max(0, (available - _window) / _hop);
        var silenceBefore = _keyDown ? 0 : Math.Max(0, _stateMs - replayHops * HopMs);
        _keyDown = false;
        _candidateHops = 0;
        // Знаки, начатые на прежнем тоне (или на помехе), к новому не относятся
        _undecided.Clear();
        // Уровни — с нуля: первое окно может быть уже внутри звука, и тогда «шум» сравнялся бы с сигналом. Шум вокруг
        // тона — по соседним частотам, их звук тона не задевает
        _levelsReady = true;
        _signal = 0;
        _noise = 0;
        _floor = replayHops > 0 ? Floor(replayHops * _hop) : Floor(0);
        _stateMs = silenceBefore;
        for (var hopsAgo = replayHops; hopsAgo >= 1; hopsAgo--)
        {
            TrackFloor(Floor(hopsAgo * _hop));
            Step(Magnitude(_toneHz, _window, null, hopsAgo * _hop));
        }
    }

    /// <summary>Один шаг 5 мс: уровни, порог с гистерезисом, смена «звук/тишина», закрытие знаков в тишине.</summary>
    private void Step(double magnitude)
    {
        TrackLevels(magnitude);
        var noise = Math.Max(_noise, _floor);
        var span = _signal - noise;
        // Сигнал — только заметно громче шума на соседних частотах: иначе «сигнал» — это пики самого шума, и в тихой
        // комнате декодер печатал бы мусор
        var valid = _signal > 1e-4 && _signal > noise * 2.5 && _signal > _floor * SignalOverFloor;
        // Гистерезис: звук начинается выше 55 % размаха, а заканчивается ниже 30 % — провал громкости посреди тире
        // (замирания, шум) не рвёт его на две точки
        var high = Math.Max(noise + span * 0.55, _floor * MarkOverFloor);
        var low = noise + span * 0.3;
        _level = valid && span > 0 ? Math.Clamp((magnitude - noise) / span, 0, 1) : 0;
        // Звучащий знак заканчивается только спадом громкости: у слабого сигнала «заметно громче шума» мигает, и знак
        // рвался бы на куски, а по кускам сбивалась бы длина точки
        var wantDown = _keyDown ? magnitude > low : valid && magnitude > high;
        // Смена состояния — только если держится подряд четверть точки (не меньше двух шагов): щелчки и провалы шума
        // не рвут знак, а на медленной скорости длинные знаки не дробятся шумом
        if (wantDown != _keyDown)
        {
            var needed = _dotMs > 0 ? Math.Clamp((int)Math.Round(_dotMs / 4 / HopMs), 2, 6) : 2;
            if (++_candidateHops >= needed)
            {
                // Оба фронта запаздывают одинаково, поэтому длительности не искажаются
                var duration = _stateMs;
                _keyDown = wantDown;
                _candidateHops = 0;
                _stateMs = 0;
                if (_keyDown)
                {
                    // Пауза засчитывается вместе со знаком: если «знак» окажется щелчком, пауза просто продолжится
                    _gapBeforeMark = duration;
                }
                else if (!EndMark(duration))
                {
                    _stateMs = _gapBeforeMark + duration;
                }
            }
        }
        else
        {
            _candidateHops = 0;
        }

        _stateMs += HopMs;
        if (!_keyDown)
        {
            Idle(_stateMs);
        }
    }

    /// <summary>Знак закончился: пауза перед ним и он сам — в разбор; false — это был щелчок, а не знак.</summary>
    private bool EndMark(double ms)
    {
        // Короче 8 мс или трети точки — щелчок или всплеск шума, не знак
        if (ms < 8 || (_dotMs > 0 && ms < _dotMs * 0.3))
        {
            return false;
        }

        OnGap(_gapBeforeMark);
        OnMark(ms);
        return true;
    }

    /// <summary>Шум рядом с тоном: быстро вверх (хлопок, речь — шум по всем частотам), вниз за ~0,1 с.</summary>
    private void TrackFloor(double floor)
    {
        _floor += (floor - _floor) * (floor > _floor ? 0.3 : 0.05);
    }

    /// <summary>
    /// Шум рядом с тоном за окно тона, samplesAgo назад: частоты ±150 и ±250 Гц (у низкого тона — выше него), среднее
    /// двух средних из четырёх — одна помеха (другая станция, гул сети) не мешает. В единицах амплитуды тона: окно Ханна
    /// ослабляет шум в √(3/8) раза, это возвращается.
    /// </summary>
    private double Floor(int samplesAgo)
    {
        Span<double> values = stackalloc double[4];
        ReadOnlySpan<int> offsets = [-250, -150, 150, 250];
        for (var index = 0; index < offsets.Length; index++)
        {
            var hz = _toneHz + offsets[index];
            if (hz < 200)
            {
                hz = _toneHz + 200 - offsets[index];
            }

            values[index] = Magnitude(hz, _window, _windowHann, samplesAgo);
        }

        values.Sort();
        return (values[1] + values[2]) / 2 / Math.Sqrt(3.0 / 8);
    }

    private void TrackLevels(double magnitude)
    {
        if (!_levelsReady)
        {
            _signal = magnitude;
            _noise = magnitude;
            _levelsReady = true;
            return;
        }

        // Сигнал: мгновенно вверх, вниз за ~2 с — замирания не теряют порог, а после долгой тишины шум не «звучит»
        _signal = magnitude > _signal ? magnitude : _signal + (magnitude - _signal) * HopSeconds / 2.0;
        // Шум: быстро вниз, вверх за ~3 с — долгое тире не поднимает его до уровня сигнала
        _noise = magnitude < _noise ? _noise + (magnitude - _noise) * 0.3 : _noise + (magnitude - _noise) * HopSeconds / 3.0;
    }

    /// <summary>Поиск тона; true — найден новый тон (раньше не было или сменилась станция).</summary>
    private bool SearchTone(bool wide)
    {
        var from = wide ? MinToneHz : Math.Max(MinToneHz, _toneHz - 60);
        var to = wide ? MaxToneHz : Math.Min(MaxToneHz, _toneHz + 60);
        var step = wide ? 10 : 5;
        var magnitudes = new List<(double Hz, double Value)>();
        for (var hz = from; hz <= to; hz += step)
        {
            magnitudes.Add((hz, Magnitude(hz, _searchLength, _hann)));
        }

        var best = magnitudes.MaxBy(item => item.Value);
        var sorted = magnitudes.Select(item => item.Value).OrderBy(value => value).ToArray();
        var median = sorted[sorted.Length / 2];
        // Тон — пик заметно выше фона диапазона и соседних частот: в шуме спектр ровный, а у «комнатного» шума и гула
        // низкие частоты громче всего диапазона, но не громче своих соседей
        var neighbours = (Magnitude(best.Hz - 150, _searchLength, _hann) + Magnitude(best.Hz - 100, _searchLength, _hann)
                          + Magnitude(best.Hz + 100, _searchLength, _hann) + Magnitude(best.Hz + 150, _searchLength, _hann)) / 4;
        if (best.Value < 1e-4 || best.Value < median * (wide ? PeakOverMedian : 3) || best.Value < neighbours * PeakOverNeighbours)
        {
            _candidateCount = 0;
            return false;
        }

        if (wide && (_toneHz <= 0 || Math.Abs(best.Hz - _toneHz) > 60))
        {
            // Новый тон — только если пик держится несколько поисков подряд на той же частоте: всплеск шума тоном не станет
            _candidateCount = _candidateCount > 0 && Math.Abs(best.Hz - _candidateHz) <= 20 ? _candidateCount + 1 : 1;
            _candidateHz = best.Hz;
            if (_candidateCount < ToneConfirmations)
            {
                return false;
            }

            _candidateCount = 0;
            _marksSinceTone = 0;
            _toneHz = best.Hz;
            return true;
        }

        // Тот же тон (дрейф) — плавно и только по звучащему знаку: пик в паузе — это шум
        _candidateCount = 0;
        if (_keyDown)
        {
            _toneHz += (best.Hz - _toneHz) * 0.5;
        }

        return false;
    }

    /// <summary>Амплитуда частоты за length отсчётов, закончившихся samplesAgo назад (алгоритм Гёрцеля); hann — окно или null.</summary>
    private double Magnitude(double frequency, int length, float[]? hann, int samplesAgo = 0)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / _sampleRate);
        double previous = 0, beforePrevious = 0;
        var start = _historyPos - samplesAgo - length + 2 * _history.Length;
        for (var index = 0; index < length; index++)
        {
            var sample = _history[(start + index) % _history.Length];
            if (hann is not null)
            {
                sample *= hann[index];
            }

            var current = sample + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
        return Math.Sqrt(Math.Max(0, power)) / length;
    }

    private void OnMark(double ms)
    {
        _marksSinceTone++;
        Remember(_recentMarks, ms);
        if (_dotMs <= 0)
        {
            _undecided.Add((true, ms));
            LearnDot(force: false);
            return;
        }

        AddElement(ms);
    }

    private void OnGap(double ms)
    {
        if (_dotMs <= 0)
        {
            // До первой отметки паузы не считаются; очень долгая тишина — начало новой передачи
            if (_undecided.Count > 0 && ms < 5000)
            {
                _undecided.Add((false, ms));
            }

            return;
        }

        RecordGap(ms);
    }

    /// <summary>Пауза закончилась: к разбору — внутри знака, между знаками или группами.</summary>
    private void RecordGap(double ms)
    {
        var units = ms / _dotMs;
        if (units < SymbolGapUnits)
        {
            _elementGaps.Add(ms);
            return;
        }

        if (units < 20)
        {
            Remember(_recentLongGaps, units);
        }

        if (units < GroupGapUnits())
        {
            _symbolGaps.Add(ms);
        }
        else if (units < 20)
        {
            _groupGaps.Add(ms);
        }
    }

    /// <summary>Тишина длится ms: символ закрывается после 2 точек, пробел ставится один раз после паузы группы.</summary>
    private void Idle(double ms)
    {
        if (_dotMs <= 0)
        {
            return;
        }

        if (ms >= _dotMs * SymbolGapUnits)
        {
            CommitSymbol();
        }

        if (!_spaceAdded && _text.Length > 0 && ms >= _dotMs * GroupGapUnits())
        {
            _text.Append(' ');
            _spaceAdded = true;
        }
    }

    /// <summary>
    /// Порог «между группами» в точках: обычно 4,6 (между 3 и 7), в самом начале приёма — 6,5, а при растянутых паузах (Фарнсворт) — в полтора раза
    /// длиннее обычной паузы между знаками. Паузы между знаками — нижняя часть длинных пауз (их в группе больше).
    /// </summary>
    private double GroupGapUnits()
    {
        // Пока длинных пауз мало, неизвестно, растянуты ли паузы между знаками (Фарнсворт, 5 точек) — порог осторожный
        if (_recentLongGaps.Count < 4)
        {
            return 6.5;
        }

        var sorted = _recentLongGaps.OrderBy(units => units).ToArray();
        var typicalSymbolGap = sorted[sorted.Length / 5];
        return Math.Max(MinGroupGapUnits, typicalSymbolGap * 1.5);
    }

    /// <summary>
    /// Длина точки по первым знакам: если среди нажатий есть и короткие, и длинные (скачок от 1,8 раза) — по ним;
    /// иначе по паузам внутри знака (они длиной в точку). Потом накопленные знаки расшифровываются задним числом.
    /// </summary>
    private void LearnDot(bool force)
    {
        var marks = _undecided.Where(item => item.IsMark).Select(item => item.Ms).OrderBy(ms => ms).ToArray();
        if (marks.Length == 0)
        {
            return;
        }

        double dot = 0;
        var split = SplitDotsAndDashes(marks);
        if (split > 0)
        {
            dot = (marks.Take(split).Sum() + marks.Skip(split).Sum() / 3) / marks.Length;
        }
        else
        {
            var gaps = _undecided.Where(item => !item.IsMark).Select(item => item.Ms).OrderBy(ms => ms).ToArray();
            if (marks.Length >= 3 && gaps.Length >= 2)
            {
                var elementGap = gaps[0];
                var typical = marks[marks.Length / 2];
                dot = typical < elementGap * 2 ? typical : typical / 3;
            }
            else if (force || marks.Length >= 12)
            {
                dot = marks[marks.Length / 2];
            }
        }

        if (dot <= 0)
        {
            return;
        }

        _dotMs = Math.Clamp(dot, 20, 400);
        var events = _undecided.ToArray();
        _undecided.Clear();
        foreach (var (isMark, ms) in events)
        {
            if (isMark)
            {
                AddElement(ms);
            }
            else
            {
                Idle(ms);
                RecordGap(ms);
            }
        }
    }

    /// <summary>
    /// Индекс первого тире в отсортированных длительностях: скачок от 1,8 раза, и тире (медиана) в 2–4,5 раза длиннее
    /// точки — как в морзянке; одиночный обрезанный или затянутый звук так не делит нажатия неправильно. Иначе 0.
    /// </summary>
    private static int SplitDotsAndDashes(IReadOnlyList<double> sorted)
    {
        var bestIndex = 0;
        var bestRatio = 1.8;
        for (var index = 1; index < sorted.Count; index++)
        {
            var ratio = sorted[index] / Math.Max(1, sorted[index - 1]);
            if (ratio < bestRatio)
            {
                continue;
            }

            var dot = sorted[index / 2];
            var dash = sorted[index + (sorted.Count - index) / 2];
            if (dash / Math.Max(1, dot) is >= 2 and <= 4.5)
            {
                bestRatio = ratio;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private void AddElement(double ms)
    {
        var isDash = ms >= _dotMs * DashUnits;
        (isDash ? _dashes : _dots).Add(ms);
        _code.Append(isDash ? '-' : '.');
        _spaceAdded = false;
        // Скорость подстраивается по каждому нажатию; раз в 8 нажатий — пересчёт по последним (смена скорости)
        _dotMs += ((isDash ? ms / 3 : ms) - _dotMs) * 0.15;
        if (++_marksSinceAnchor >= 8)
        {
            _marksSinceAnchor = 0;
            var sorted = _recentMarks.OrderBy(value => value).ToArray();
            var split = SplitDotsAndDashes(sorted);
            if (split > 0)
            {
                _dotMs = (sorted.Take(split).Sum() + sorted.Skip(split).Sum() / 3) / sorted.Length;
            }
        }

        _dotMs = Math.Clamp(_dotMs, 20, 400);
    }

    private void CommitSymbol()
    {
        if (_code.Length == 0)
        {
            return;
        }

        // Незнакомый код — «*»: видно, что знак был, но не разобран
        _text.Append(MorseAlphabet.TryGetSymbol(_code.ToString(), _alphabet, out var symbol) ? symbol : '*');
        _code.Clear();
    }

    private static void Remember(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > RecentCount)
        {
            list.RemoveAt(0);
        }
    }
}
