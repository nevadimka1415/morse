using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Maui.Devices;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile.Services;

/// <summary>
/// «Обратная связь» в «Настройках»: сообщение уходит разработчику на его сервер (оттуда — письмом), без отзывов в
/// магазине. Отправляется только по кнопке и только то, что видно в форме, плюс версия приложения, система и язык —
/// чтобы понять, о какой сборке речь. Ничего другого с устройства не уходит.
/// </summary>
public static class FeedbackService
{
    private const string Endpoint = "https://mirpoehal.ru/morse/otzyv";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task SendAsync(string text, string contact, string appVersion)
    {
        var message = new FeedbackMessage(text.Trim(), contact.Trim(), appVersion,
            $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}", Texts.IsEnglish ? "en" : "ru",
#if RUSTORE
            "RuStore");
#else
            "GitHub");
#endif
        using var response = await Client.PostAsJsonAsync(Endpoint, message);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Тело запроса: имена полей — как их ждёт сервер (/morse/otzyv).</summary>
public sealed record FeedbackMessage(
    [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
    [property: System.Text.Json.Serialization.JsonPropertyName("contact")] string Contact,
    [property: System.Text.Json.Serialization.JsonPropertyName("version")] string Version,
    [property: System.Text.Json.Serialization.JsonPropertyName("platform")] string Platform,
    [property: System.Text.Json.Serialization.JsonPropertyName("language")] string Language,
    [property: System.Text.Json.Serialization.JsonPropertyName("channel")] string Channel);
