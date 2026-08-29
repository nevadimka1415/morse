namespace MorseTrainer.Domain;

public sealed record LearningSymbolItem(char Symbol, string Code, string Chant, string Category);

public static class LearningCatalog
{
    private static readonly IReadOnlyDictionary<char, string> RussianChants = new Dictionary<char, string>
    {
        ['А'] = "ай-даа",
        ['Б'] = "баа-ки-те-кут",
        ['В'] = "ви-даа-лаа",
        ['Г'] = "гаа-раа-жи",
        ['Д'] = "доо-ми-ки",
        ['Е'] = "есть",
        ['Ё'] = "есть",
        ['Ж'] = "же-ле-зис-тоо",
        ['З'] = "заа-каа-ти-ки",
        ['И'] = "и-ди",
        ['Й'] = "ий-краааа-ткааааа-яяяяяя",
        ['К'] = "каааак-де-лаааааа",
        ['Л'] = "лу-наа-ти-ки",
        ['М'] = "маа-маа",
        ['Н'] = "ноо-мер",
        ['О'] = "оо-коо-лоо",
        ['П'] = "пи-лаа-поо-ёт",
        ['Р'] = "ре-шаа-ет",
        ['С'] = "си-ни-е",
        ['Т'] = "таак",
        ['У'] = "у-нес-лоо",
        ['Ф'] = "фи-ли-моон-чик",
        ['Х'] = "хи-ми-чи-те",
        ['Ц'] = "цаааап-ля-цааааап-ля",
        ['Ч'] = "чаа-шаа-тоо-нет",
        ['Ш'] = "шаа-роо-ваа-рыы",
        ['Щ'] = "щаа-ваам-не-шаа",
        ['Ъ'] = "твёёр-дыый-не-мяяг-киий",
        ['Ы'] = "ыы-не-наа-доо",
        ['Ь'] = "знааак-мяг-кий-знааааак",
        ['Э'] = "э-ле-ктроо-ни-ка",
        ['Ю'] = "ю-ли-аа-наа",
        ['Я'] = "я-маал-я-маал"
    };

    private static readonly IReadOnlyDictionary<char, string> DigitChants = new Dictionary<char, string>
    {
        ['1'] = "и-тооль-коо-оо-днаа",
        ['2'] = "две-не-хоо-роо-шоо",
        ['3'] = "три-те-бе-маа-лоо",
        ['4'] = "че-тве-ри-те-каа",
        ['5'] = "пя-ти-ле-ти-е",
        ['6'] = "поо-шес-ти-бе-ри",
        ['7'] = "даа-даа-се-ме-ри",
        ['8'] = "воо-сьмоо-гоо-и-ди",
        ['9'] = "ноо-наа-ноо-наа-ми",
        ['0'] = "нооль-тоо-оо-коо-лоо"
    };

    private static readonly IReadOnlyDictionary<string, string> ChantByCode = BuildChantByCode();

    public static IReadOnlyList<LearningSymbolItem> Russian { get; } = RussianChants
        .Select(item => new LearningSymbolItem(item.Key, MorseAlphabet.Russian[item.Key], item.Value, "Русские буквы"))
        .ToArray();

    public static IReadOnlyList<LearningSymbolItem> Latin { get; } = MorseAlphabet.Latin
        .Select(item => new LearningSymbolItem(item.Key, item.Value, ChantByCode[item.Value], "Латинские буквы"))
        .ToArray();

    public static IReadOnlyList<LearningSymbolItem> Digits { get; } = DigitChants
        .Select(item => new LearningSymbolItem(item.Key, MorseAlphabet.Digits[item.Key], item.Value, "Цифры"))
        .ToArray();

    public static IReadOnlyList<LearningSymbolItem> GetItems(int index)
    {
        return index switch
        {
            1 => Latin,
            2 => Russian.Concat(Latin).ToArray(),
            3 => Digits,
            _ => Russian
        };
    }

    public static LearningSymbolItem? Find(char symbol)
    {
        symbol = char.ToUpperInvariant(symbol);
        return Russian.Concat(Latin).Concat(Digits).FirstOrDefault(item => item.Symbol == symbol);
    }

    private static IReadOnlyDictionary<string, string> BuildChantByCode()
    {
        var result = new Dictionary<string, string>();
        foreach (var item in RussianChants)
        {
            result.TryAdd(MorseAlphabet.Russian[item.Key], item.Value);
        }

        return result;
    }
}
