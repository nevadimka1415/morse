using Android.Media;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Platforms.Android;

/// <summary>
/// Микрофон для декодера: AudioRecord, 16 кГц, моно, 16 бит. Источник VoiceRecognition — на большинстве телефонов
/// без автоусиления и шумоподавления, которые могут «съесть» тон; если он недоступен — обычный Mic.
/// </summary>
public sealed class PlatformMicrophoneStream : IMicrophoneStream
{
    private const int SampleRate = 16_000;
    private AudioRecord? _record;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    // Цикл чтения заканчивается и сам, если микрофон отобрала система — тогда запись уже не идёт
    public bool IsRunning => _record is not null && _loop is { IsCompleted: false };

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
        var minimum = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, global::Android.Media.Encoding.Pcm16bit);
        var size = Math.Max(minimum, SampleRate / 5 * 2);
        var record = Open(AudioSource.VoiceRecognition, size) ?? Open(AudioSource.Mic, size)
                     ?? throw new InvalidOperationException("AudioRecord is not initialized");
        record.StartRecording();
        _record = record;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _loop = Task.Factory.StartNew(() =>
        {
            // Порции по 50 мс
            var shorts = new short[SampleRate / 20];
            var floats = new float[shorts.Length];
            while (!cancellation.IsCancellationRequested)
            {
                var read = record.Read(shorts, 0, shorts.Length);
                if (read < 0)
                {
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    floats[index] = shorts[index] / 32768f;
                }

                if (read > 0)
                {
                    onSamples(floats, read);
                }
            }
        }, cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return SampleRate;
    }

    public void Stop()
    {
        var record = _record;
        _record = null;
        _cancellation?.Cancel();
        if (record is null)
        {
            return;
        }

        try
        {
            record.Stop();
            // После Stop чтение возвращает управление — дожидаемся цикла, потом освобождаем запись
            _loop?.Wait(500);
        }
        catch (Exception)
        {
            // Микрофон уже отпущен системой
        }

        record.Release();
        record.Dispose();
    }

    private static AudioRecord? Open(AudioSource source, int size)
    {
        var record = new AudioRecord(source, SampleRate, ChannelIn.Mono, global::Android.Media.Encoding.Pcm16bit, size);
        if (record.State == global::Android.Media.State.Initialized)
        {
            return record;
        }

        record.Release();
        record.Dispose();
        return null;
    }
}
