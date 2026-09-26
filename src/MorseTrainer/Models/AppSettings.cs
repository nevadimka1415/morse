namespace MorseTrainer.Models;

public sealed class AppSettings
{
    public int AlphabetIndex { get; set; } = 0;
    public int ContentModeIndex { get; set; } = 2;
    public int GroupCount { get; set; } = 10;
    public int CharactersPerMinute { get; set; } = 60;
    public int FrequencyHz { get; set; } = 700;
    public int VolumePercent { get; set; } = 70;
    public int CharacterGapUnits { get; set; } = 3;
    public int GroupGapUnits { get; set; } = 7;
    public int StartPauseUnits { get; set; } = 21;
    public bool PlayStartSignal { get; set; } = true;
    // Помехи эфира: шум, замирания, дрейф тона (0 — чистый сигнал)
    public int NoisePercent { get; set; }
    public int QsbPercent { get; set; }
    public int DriftHz { get; set; }
    public string CustomSymbols { get; set; } = "АБВГДЕЖЗИКЛМНОПРСТУ";
    public int KochLevel { get; set; } = 2;
    public bool EmphasizeProblemSymbols { get; set; } = true;
    // Лестница скорости: +5 после двух заданий от 90 %, −5 при точности ниже 70 %
    public bool AutoSpeed { get; set; }
    // Экзамен: разрешённые прослушивания (1–3) и лимит времени в минутах (0 — без лимита)
    public int ExamPlaybacks { get; set; } = 1;
    public int ExamTimeLimitMinutes { get; set; }
    // Цель на день в минутах тренировки (0 — без цели)
    public int DailyGoalMinutes { get; set; } = 10;
    // Курс «С нуля до 60 зн/мин»: текущий шаг (0 — курс не начат)
    public int CourseStep { get; set; }
    // Напоминание о тренировке на телефоне: включено и время дня в минутах от полуночи
    public bool ReminderEnabled { get; set; }
    public int ReminderMinutes { get; set; } = 19 * 60;
    public int ThemeIndex { get; set; } = 0;
    public int LanguageIndex { get; set; } = 0;
    public int LearningAlphabetIndex { get; set; } = 0;
    public int LearningAudioModeIndex { get; set; } = 1;
    public int QuizCorrect { get; set; }
    public int QuizTotal { get; set; }
    // Проверка на слух: своя скорость сигнала и следующий символ сразу после ответа
    public int QuizSpeed { get; set; } = 45;
    /// <summary>Скорость сигнала на карточках «Обучения» (20–200 зн/мин, шаг 5): можно потренировать знаки быстрее.</summary>
    public int LearningSignalSpeed { get; set; } = 45;
    public bool QuizAutoNext { get; set; } = true;
    // «На слух»: сколько раз путали символ (ключ — символ); такие звучат чаще, верный ответ счётчик уменьшает
    public Dictionary<string, int> QuizMisses { get; set; } = new();
    // Windows: набор символов «На слух» (0 русские, 1 латинские, 2 русские и латинские, 3 цифры)
    public int QuizAlphabetIndex { get; set; }
    // Windows: поле ввода ответа на «Тренировке» (обычно пишут на бумаге — по умолчанию скрыто)
    public bool ShowAnswerInput { get; set; }
    // Когда последний раз занимались без ввода ответа (прослушано задание, ответ «На слух»): для плашки и напоминания
    public DateTime? LastPracticeAt { get; set; }
    /// <summary>Дни занятий («yyyy-MM-dd»), в том числе без записи в истории: для серии «дней подряд» в напоминании.</summary>
    public List<string> PracticeDays { get; set; } = new();
    public string ActiveProfileName { get; set; } = "Основной";
    // Windows: размер окна и открытая вкладка между запусками (0 — не сохранялось)
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public int MainTabIndex { get; set; }
    public bool TrainingPanelCollapsed { get; set; }
}
