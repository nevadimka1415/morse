using Android.Media;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Platforms.Android;

public sealed class PlatformAudioPlaybackService : IAudioPlaybackService
{
    private MediaPlayer? _player;
    private TaskCompletionSource? _completion;
    public bool IsPlaying => _player?.IsPlaying == true;

    public async Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        Stop();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        var player = new MediaPlayer();
        _player = player;
        player.SetAudioAttributes(new AudioAttributes.Builder()!
            .SetContentType(AudioContentType.Music)!
            .SetUsage(AudioUsageKind.Media)!
            .Build());
        player.SetDataSource(filePath);
        player.Completion += (_, _) => completion.TrySetResult();
        player.Error += (_, args) =>
        {
            completion.TrySetException(new InvalidOperationException($"Android audio error: {args.What}"));
            args.Handled = true;
        };
        player.Prepare();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            Stop();
            completion.TrySetCanceled(cancellationToken);
        });
        player.Start();
        try
        {
            await completion.Task;
        }
        finally
        {
            Stop();
        }
    }

    public void StartLoop(string filePath)
    {
        Stop();
        var player = new MediaPlayer();
        _player = player;
        player.SetAudioAttributes(new AudioAttributes.Builder()!
            .SetContentType(AudioContentType.Music)!
            .SetUsage(AudioUsageKind.Media)!
            .Build());
        player.SetDataSource(filePath);
        player.Looping = true;
        player.Prepare();
        player.Start();
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

        try
        {
            if (_player.IsPlaying)
            {
                _player.Stop();
            }
        }
        catch
        {
            // The platform player may already be released after a completion callback.
        }

        _player.Release();
        _player.Dispose();
        _player = null;
    }
}
