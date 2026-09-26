namespace MorseTrainer.Mobile.Services;

/// <summary>Звук с микрофона для декодера: отсчёты −1…1 приходят порциями из фонового потока.</summary>
public interface IMicrophoneStream
{
    /// <summary>Микрофон слушает; false и после того, как система его отобрала (звонок, другое приложение).</summary>
    bool IsRunning { get; }

    /// <summary>Разрешение на микрофон; false — не дали.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>Запуск: onSamples(буфер, сколько) вызывается из фонового потока. Возвращает частоту дискретизации.</summary>
    int Start(Action<float[], int> onSamples);

    void Stop();
}
