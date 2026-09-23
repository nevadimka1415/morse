using Android.Media;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Platforms.Android;

public sealed class PlatformAudioRecorderService : IAudioRecorderService
{
    private MediaRecorder? _recorder;

    public bool IsRecording => _recorder is not null;

    public async Task<bool> RequestPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        }

        return status == PermissionStatus.Granted;
    }

    public void Start(string filePath)
    {
        Stop();
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        // Android 12+ требует контекст в конструкторе; на старых версиях — конструктор без параметров
        var recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new MediaRecorder(global::Android.App.Application.Context)
#pragma warning disable CA1422
            : new MediaRecorder();
#pragma warning restore CA1422
        try
        {
            recorder.SetAudioSource(AudioSource.Mic);
            recorder.SetOutputFormat(OutputFormat.Mpeg4);
            recorder.SetAudioEncoder(AudioEncoder.Aac);
            recorder.SetAudioChannels(1);
            recorder.SetAudioSamplingRate(44100);
            recorder.SetAudioEncodingBitRate(128000);
            recorder.SetOutputFile(filePath);
            recorder.Prepare();
            recorder.Start();
        }
        catch
        {
            recorder.Release();
            recorder.Dispose();
            throw;
        }

        _recorder = recorder;
    }

    public bool Stop()
    {
        var recorder = _recorder;
        _recorder = null;
        if (recorder is null)
        {
            return false;
        }

        try
        {
            recorder.Stop();
            return true;
        }
        catch (Java.Lang.RuntimeException)
        {
            // MediaRecorder бросает исключение, если данных не набралось (остановили сразу)
            return false;
        }
        finally
        {
            recorder.Release();
            recorder.Dispose();
        }
    }
}
