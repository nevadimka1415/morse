using System.Runtime.InteropServices;
using AVFoundation;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Platforms.iOS;

/// <summary>Микрофон для декодера: AVAudioEngine, режим «измерение» — без обработки голоса, тон не искажается.</summary>
public sealed class PlatformMicrophoneStream : IMicrophoneStream
{
    private AVAudioEngine? _engine;

    // Звонок или другое приложение останавливают движок — тогда запись уже не идёт
    public bool IsRunning => _engine is { Running: true };

    public async Task<bool> RequestPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        }

        return status == PermissionStatus.Granted;
    }

    public int Start(Action<float[], int> onSamples)
    {
        Stop();
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker);
        session.SetMode(AVAudioSession.ModeMeasurement, out _);
        session.SetActive(true);
        var engine = new AVAudioEngine();
        var input = engine.InputNode;
        var format = input.GetBusOutputFormat(0);
        var buffer = new float[16_384];
        input.InstallTapOnBus(0, 1024, format, (pcm, _) =>
        {
            var count = Math.Min((int)pcm.FrameLength, buffer.Length);
            if (count <= 0 || pcm.FloatChannelData == IntPtr.Zero)
            {
                return;
            }

            // Первый канал: FloatChannelData — массив указателей на каналы
            Marshal.Copy(Marshal.ReadIntPtr(pcm.FloatChannelData), buffer, 0, count);
            onSamples(buffer, count);
        });
        engine.Prepare();
        if (!engine.StartAndReturnError(out var error))
        {
            input.RemoveTapOnBus(0);
            engine.Dispose();
            throw new InvalidOperationException(error?.LocalizedDescription ?? "AVAudioEngine did not start");
        }

        _engine = engine;
        return (int)format.SampleRate;
    }

    public void Stop()
    {
        var engine = _engine;
        _engine = null;
        if (engine is null)
        {
            return;
        }

        engine.InputNode.RemoveTapOnBus(0);
        engine.Stop();
        engine.Dispose();
        // Вернуть сессию к обычному воспроизведению: иначе режим «измерение» и запись остаются для остальных звуков
        var session = AVAudioSession.SharedInstance();
        session.SetMode(AVAudioSession.ModeDefault, out _);
        session.SetCategory(AVAudioSessionCategory.Playback);
    }
}
