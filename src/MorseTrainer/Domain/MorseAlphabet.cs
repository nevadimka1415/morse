using System.Collections.ObjectModel;

namespace MorseTrainer.Domain;

public enum AlphabetMode
{
    Russian,
    Latin,
    RussianAndLatin
}

public enum ContentMode
{
    Letters,
    Digits,
    LettersAndDigits,
    AllSymbols,
    Custom
}

public static class MorseAlphabet
{
    public static readonly IReadOnlyDictionary<char, string> Russian =
        new ReadOnlyDictionary<char, string>(new Dictionary<char, string>
        {
            ['А'] = ".-", ['Б'] = "-...", ['В'] = ".--", ['Г'] = "--.",
            ['Д'] = "-..", ['Е'] = ".", ['Ё'] = ".", ['Ж'] = "...-",
            ['З'] = "--..", ['И'] = "..", ['Й'] = ".---", ['К'] = "-.-",
            ['Л'] = ".-..", ['М'] = "--", ['Н'] = "-.", ['О'] = "---",
            ['П'] = ".--.", ['Р'] = ".-.", ['С'] = "...", ['Т'] = "-",
            ['У'] = "..-", ['Ф'] = "..-.", ['Х'] = "....", ['Ц'] = "-.-.",
            ['Ч'] = "---.", ['Ш'] = "----", ['Щ'] = "--.-", ['Ъ'] = "--.--",
            ['Ы'] = "-.--", ['Ь'] = "-..-", ['Э'] = "..-..", ['Ю'] = "..--",
            ['Я'] = ".-.-"
        });

    public static readonly IReadOnlyDictionary<char, string> Latin =
        new ReadOnlyDictionary<char, string>(new Dictionary<char, string>
        {
            ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..",
            ['E'] = ".", ['F'] = "..-.", ['G'] = "--.", ['H'] = "....",
            ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..",
            ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.",
            ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-",
            ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
            ['Y'] = "-.--", ['Z'] = "--.."
        });

    public static readonly IReadOnlyDictionary<char, string> Digits =
        new ReadOnlyDictionary<char, string>(new Dictionary<char, string>
        {
            ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--",
            ['4'] = "....-", ['5'] = ".....", ['6'] = "-....", ['7'] = "--...",
            ['8'] = "---..", ['9'] = "----."
        });

    public static readonly IReadOnlyDictionary<char, string> Punctuation =
        new ReadOnlyDictionary<char, string>(new Dictionary<char, string>
        {
            ['.'] = ".-.-.-", [','] = "--..--", ['?'] = "..--..", ['!'] = "-.-.--",
            ['/'] = "-..-.", ['('] = "-.--.", [')'] = "-.--.-", [':'] = "---...",
            [';'] = "-.-.-.", ['='] = "-...-", ['+'] = ".-.-.", ['-'] = "-....-",
            ['_'] = "..--.-", ['@'] = ".--.-."
        });

    public static bool TryGetCode(char symbol, out string code)
    {
        symbol = char.ToUpperInvariant(symbol);
        return Russian.TryGetValue(symbol, out code!)
            || Latin.TryGetValue(symbol, out code!)
            || Digits.TryGetValue(symbol, out code!)
            || Punctuation.TryGetValue(symbol, out code!);
    }

    public static IReadOnlyList<char> BuildPool(
        AlphabetMode alphabet,
        ContentMode content,
        string customSymbols)
    {
        if (content == ContentMode.Custom)
        {
            return FilterSupportedSymbols(customSymbols);
        }

        var pool = new List<char>();
        if (content is ContentMode.Letters or ContentMode.LettersAndDigits or ContentMode.AllSymbols)
        {
            if (alphabet is AlphabetMode.Russian or AlphabetMode.RussianAndLatin)
            {
                pool.AddRange(Russian.Keys);
            }

            if (alphabet is AlphabetMode.Latin or AlphabetMode.RussianAndLatin)
            {
                pool.AddRange(Latin.Keys);
            }
        }

        if (content is ContentMode.Digits or ContentMode.LettersAndDigits or ContentMode.AllSymbols)
        {
            pool.AddRange(Digits.Keys);
        }

        if (content == ContentMode.AllSymbols)
        {
            pool.AddRange(Punctuation.Keys);
        }

        return pool.Distinct().ToArray();
    }

    public static IReadOnlyList<char> FilterSupportedSymbols(string symbols)
    {
        return symbols
            .ToUpperInvariant()
            .Where(symbol => !char.IsWhiteSpace(symbol) && TryGetCode(symbol, out _))
            .Distinct()
            .ToArray();
    }
}
