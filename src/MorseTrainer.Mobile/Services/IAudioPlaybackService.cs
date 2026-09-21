namespace MorseTrainer.Mobile.Services;

public interface IAudioPlaybackService
{
    bool IsPlaying { get; }
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
    /// <summary>Зацикленный тон для ручного ключа; останавливается методом Stop.</summary>
    void StartLoop(string filePath);
    void Stop();
}
