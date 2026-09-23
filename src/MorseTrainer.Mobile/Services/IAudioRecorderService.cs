namespace MorseTrainer.Mobile.Services;

/// <summary>Запись своего напева с микрофона в файл .m4a (AAC, моно, 44,1 кГц).</summary>
public interface IAudioRecorderService
{
    bool IsRecording { get; }

    /// <summary>Спрашивает доступ к микрофону; false — пользователь запретил.</summary>
    Task<bool> RequestPermissionAsync();

    void Start(string filePath);

    /// <summary>Останавливает запись; false — запись не получилась (например, остановлена сразу же).</summary>
    bool Stop();
}
