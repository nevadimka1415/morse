using AVFoundation;
using Foundation;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Platforms.iOS;

public sealed class PlatformAudioPlaybackService : IAudioPlaybackService
{
    private AVAudioPlayer? _player;
    private TaskCompletionSource? _completion;
    public bool IsPlaying => _player?.Playing == true;

    public async Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        Stop();
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Playback);
        session.SetActive(true);

        var player = AVAudioPlayer.FromUrl(NSUrl.FromFilename(filePath));
        if (player is null)
        {
            throw new InvalidOperationException("iOS could not open the audio file.");
        }

        _player = player;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        player.FinishedPlaying += (_, _) => completion.TrySetResult();
        if (!player.PrepareToPlay() || !player.Play())
        {
            Stop();
            throw new InvalidOperationException("iOS could not start audio playback.");
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            // Отмена старого воспроизведения не должна глушить уже запущенное новое
            if (ReferenceEquals(_player, player))
            {
                Stop();
            }

            completion.TrySetCanceled(cancellationToken);
        });
        try
        {
            await completion.Task;
        }
        finally
        {
            // Новый PlayAsync поверх этого уже остановил этот плеер, а _player — уже новый: его не трогаем
            if (ReferenceEquals(_player, player))
            {
                Stop();
            }
        }
    }

    public void StartLoop(string filePath)
    {
        Stop();
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Playback);
        session.SetActive(true);
        var player = AVAudioPlayer.FromUrl(NSUrl.FromFilename(filePath));
        if (player is null)
        {
            throw new InvalidOperationException("iOS could not open the tone file.");
        }

        _player = player;
        player.NumberOfLoops = -1;
        if (!player.PrepareToPlay() || !player.Play())
        {
            Stop();
            throw new InvalidOperationException("iOS could not start the tone.");
        }
    }

    public void Stop()
    {
        var completion = _completion;
        _completion = null;
        completion?.TrySetCanceled();
        if (_player is null)
        {
            return;
        }

        if (_player.Playing)
        {
            _player.Stop();
        }

        _player.Dispose();
        _player = null;
    }
}
