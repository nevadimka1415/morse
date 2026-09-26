using System.Security.Cryptography;

namespace MorseTrainer.Domain;

/// <summary>
/// Словари режимов «Слова», «Позывные» и «Q-код и сокращения». Общие для Windows и телефона.
/// Слова только из букв с кодом Морзе (без Ё: её код совпадает с Е).
/// </summary>
public static class WordLists
{
    public static IReadOnlyList<string> RussianWords { get; } = Split(
        "МАМА ПАПА ДОМ ЛЕС РЕКА МОРЕ ГОРА ПОЛЕ САД ГОРОД СЕЛО ДОРОГА МОСТ ОКНО ДВЕРЬ СТОЛ СТУЛ КНИГА ШКОЛА УРОК " +
        "СЛОВО БУКВА ЗВУК СВЯЗЬ РАДИО ВОЛНА ЭФИР СИГНАЛ ПРИЕМ КЛЮЧ ТОЧКА ТИРЕ ПАУЗА КОД ТЕКСТ ЧИСЛО ЦИФРА ЗНАК ВОПРОС ОТВЕТ " +
        "ВРЕМЯ ЧАС ДЕНЬ НОЧЬ УТРО ВЕЧЕР ЗИМА ВЕСНА ЛЕТО ОСЕНЬ СНЕГ ДОЖДЬ ВЕТЕР ГРОЗА ТУМАН ЖАРА ХОЛОД СОЛНЦЕ ЛУНА ЗВЕЗДА " +
        "НЕБО ЗЕМЛЯ ВОДА ОГОНЬ КАМЕНЬ ПЕСОК ТРАВА ЦВЕТОК ДЕРЕВО ПТИЦА РЫБА ЗВЕРЬ КОШКА СОБАКА ЛОШАДЬ КОРОВА ХЛЕБ СОЛЬ САХАР ЧАЙ " +
        "КОФЕ МОЛОКО МЯСО СУП КАША ЯБЛОКО ГРУША ВИШНЯ РУКА НОГА ГЛАЗ УХО НОС РОТ ЗУБ СЕРДЦЕ ДРУГ БРАТ СЕСТРА СЫН ДОЧЬ ДЕД БАБА " +
        "ИМЯ ГОСТЬ РАБОТА ОТДЫХ ИГРА СПОРТ ФУТБОЛ ЛЫЖИ МЯЧ ПЕСНЯ МУЗЫКА ТАНЕЦ КИНО ТЕАТР ПОЕЗД САМОЛЕТ КОРАБЛЬ МАШИНА ЛОДКА " +
        "ЯКОРЬ ПОРТ ФЛАГ КАРТА ПУТЬ ШАГ БЕГ ПРЫЖОК СИЛА ПОМОЩЬ ПРИВЕТ СПАСИБО ПОКА УДАЧА ПОБЕДА МИР АРМИЯ ФЛОТ ПОЛК РОТА ШТАБ " +
        "ПРИКАЗ РАПОРТ СЕКРЕТ ШИФР ЦЕНТР СЕВЕР ЮГ ЗАПАД ВОСТОК МОСКВА ВОЛГА УРАЛ СИБИРЬ ТАЙГА СТЕПЬ ОСТРОВ БЕРЕГ ЗАЛИВ ВОЛК ЛИСА " +
        "ЗАЯЦ МЕДВЕДЬ БЕЛКА ЖУК МУХА ПЧЕЛА ПОДЪЕЗД ОБЪЕКТ СЪЕЗД ЭХО ЭКРАН ЮБКА ЯМА ЩИТ ЩУКА ЦЕЛЬ ФАКТ ФОРМА ХОД ШУМ ЩЕКА ЭТАЖ ЮНГА ЯХТА");

    public static IReadOnlyList<string> EnglishWords { get; } = Split(
        "THE AND YOU FOR ARE WITH THIS THAT HAVE FROM WHAT WERE WHEN YOUR SAID EACH WHICH THEIR TIME WILL ABOUT MANY THEN THEM " +
        "WRITE LIKE LONG MAKE THING LOOK MORE COME MADE OVER SOUND MOST WORD GOOD DAY NIGHT MORNING RADIO CODE MORSE KEY SIGNAL " +
        "TONE DOT DASH SPACE PAUSE SPEED LEVEL TEST EXAM CALL NAME WORLD HELLO THANKS PLEASE AGAIN REPEAT COPY SEND ANSWER NUMBER " +
        "LETTER GROUP TASK TRAIN LEARN STUDY ANTENNA POWER WATT VOLT OHM WIRE CABLE TOWER MAST BAND METER FIELD NOISE STATIC STORM " +
        "RAIN SNOW WIND CLOUD SUN MOON STAR SKY EARTH WATER FIRE STONE SAND GRASS TREE BIRD FISH CAT DOG HORSE BREAD SALT SUGAR TEA " +
        "COFFEE MILK APPLE HAND FOOT EYE EAR NOSE MOUTH HEART FRIEND BROTHER SISTER SON GIRL BOY MAN WOMAN HOUSE HOME ROOM DOOR " +
        "WINDOW TABLE CHAIR BOOK SCHOOL LESSON CITY TOWN ROAD BRIDGE RIVER SEA LAKE HILL FOREST NORTH SOUTH EAST WEST SHIP BOAT " +
        "PLANE CAR BUS MAP FLAG WAY STEP RUN JUMP HELP LUCK PEACE ARMY NAVY FLEET ORDER REPORT SECRET CIPHER CENTER ZERO ONE TWO " +
        "THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN QUICK BROWN FOX JUMPS LAZY");

    /// <summary>Q-код и радиолюбительские сокращения; несколько с вопросительным знаком.</summary>
    public static IReadOnlyList<string> QCodes { get; } = Split(
        "QRA QRG QRH QRK QRL QRM QRN QRO QRP QRQ QRS QRT QRU QRV QRX QRZ QSA QSB QSD QSK QSL QSO QSP QSY QTC QTH QTR QRZ? QTH? QRL? " +
        "CQ DE K KN AR SK BK TU TNX TKS 73 88 GM GA GE GN OM YL XYL RST RIG ANT PWR WX HR ES HW CPI AGN PSE R RR FB UR NR NAME OP HI " +
        "CUL GL GB SRI VY DX RPT WKD WPM SIG ABT ALL ANS BCNU BTU CFM CL CONDX DR FER GUD HPE HV LSN MNI NW OK RCVD SED SN SOS TMW VA WL WUD TEST");

    // Префиксы позывных: Россия и распространённые международные
    private static readonly string[] CallsignPrefixes =
    {
        "R", "RA", "RK", "RN", "RU", "RV", "RW", "RX", "RZ", "UA", "UB", "UD", "UN", "UR", "US", "EW", "EU",
        "DL", "DJ", "G", "M", "F", "I", "EA", "SP", "OK", "OM", "HA", "LZ", "YO", "S5", "9A", "OE", "HB9", "ON", "PA",
        "SM", "LA", "OH", "ES", "YL", "LY", "W", "K", "N", "AA", "KB", "VE", "VA", "JA", "JH", "VK", "ZL", "PY", "LU", "ZS", "4X"
    };

    private const string LatinLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static IReadOnlyList<string> Words(AlphabetMode alphabet)
    {
        return alphabet switch
        {
            AlphabetMode.Latin => EnglishWords,
            AlphabetMode.RussianAndLatin => RussianWords.Concat(EnglishWords).ToArray(),
            _ => RussianWords
        };
    }

    /// <summary>Случайный позывной: префикс, цифра района, суффикс из 1–3 латинских букв (R3ABC, DL1AB, W1AW).</summary>
    public static string RandomCallsign()
    {
        var prefix = CallsignPrefixes[RandomNumberGenerator.GetInt32(CallsignPrefixes.Length)];
        var digit = (char)('0' + RandomNumberGenerator.GetInt32(10));
        var roll = RandomNumberGenerator.GetInt32(100);
        var suffixLength = roll < 15 ? 1 : roll < 60 ? 2 : 3;
        var suffix = new char[suffixLength];
        for (var index = 0; index < suffixLength; index++)
        {
            suffix[index] = LatinLetters[RandomNumberGenerator.GetInt32(LatinLetters.Length)];
        }

        return prefix + digit + new string(suffix);
    }

    // Для радиообмена: имена, города, рапорты — как их передают в эфире (латиница)
    private static readonly string[] OperatorNames =
        { "ALEX", "IVAN", "OLEG", "SERGEY", "PAVEL", "YURI", "DIMA", "OLGA", "ANNA", "NINA", "JOHN", "MIKE", "TOM", "HANS", "PETER" };

    private static readonly string[] Cities =
        { "MOSCOW", "TVER", "OMSK", "PERM", "KAZAN", "SOCHI", "TULA", "KIROV", "BERLIN", "PARIS", "PRAGUE", "ROME", "OSLO", "TOKYO", "SEOUL" };

    private static readonly string[] Reports = { "599", "589", "579", "569", "559", "449" };
    private static readonly string[] Greetings = { "GM", "GA", "GE" };
    private static readonly string[] Extras =
        { "RIG IC7300 PWR 100W ANT DIPOLE", "RIG FT891 PWR 50W ANT GP", "WX SUNNY TEMP 20C", "WX CLOUDY TEMP 5C", "HR WX RAIN" };

    /// <summary>
    /// Одна связь, как она звучит в эфире: общий вызов CQ, ответ с приветствием, рапортом RST, именем и городом,
    /// подтверждение и прощание 73 … SK. Позывные случайные, всё латиницей; иногда — аппаратура или погода.
    /// </summary>
    public static string RandomExchange()
    {
        var caller = RandomCallsign();
        var answer = RandomCallsign();
        while (answer == caller)
        {
            answer = RandomCallsign();
        }

        string Pick(string[] items) => items[RandomNumberGenerator.GetInt32(items.Length)];
        var extra = RandomNumberGenerator.GetInt32(100) < 50 ? " " + Pick(Extras) : string.Empty;
        var reportToCaller = Pick(Reports);
        return $"CQ CQ DE {caller} {caller} K " +
               $"{caller} DE {answer} {Pick(Greetings)} OM TNX CALL UR RST {reportToCaller} {reportToCaller} NAME {Pick(OperatorNames)} QTH {Pick(Cities)}{extra} HW? {caller} DE {answer} KN " +
               $"{answer} DE {caller} R TNX FER RPRT UR RST {Pick(Reports)} NAME {Pick(OperatorNames)} QTH {Pick(Cities)} 73 {answer} DE {caller} SK";
    }

    /// <summary>Все символы, которые встречаются в режиме: для подсказок и умного повторения.</summary>
    public static IReadOnlyList<char> Symbols(ContentMode content, AlphabetMode alphabet)
    {
        return content switch
        {
            ContentMode.Callsigns => (LatinLetters + "0123456789").ToCharArray(),
            ContentMode.RadioExchange => (LatinLetters + "0123456789?").ToCharArray(),
            ContentMode.QCodes => QCodes.SelectMany(word => word).Distinct().OrderBy(symbol => symbol).ToArray(),
            ContentMode.Words => Words(alphabet).SelectMany(word => word).Distinct().OrderBy(symbol => symbol).ToArray(),
            _ => Array.Empty<char>()
        };
    }

    private static IReadOnlyList<string> Split(string text)
    {
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
