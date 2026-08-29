namespace MorseTrainer.Mobile.Services;

public interface IAudioPlaybackService
{
    bool IsPlaying { get; }
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
    void Stop();
}
