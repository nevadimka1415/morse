using AudioToolbox;
using AVFoundation;
using Foundation;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Platforms.iOS;

public sealed class PlatformAudioRecorderService : IAudioRecorderService
{
    private AVAudioRecorder? _recorder;

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
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker);
        session.SetActive(true);
        var settings = new AudioSettings
        {
            Format = AudioFormatType.MPEG4AAC,
            SampleRate = 44100,
            NumberChannels = 1,
            AudioQuality = AVAudioQuality.High
        };
        var recorder = AVAudioRecorder.Create(NSUrl.FromFilename(filePath), settings, out var error);
        if (recorder is null || error is not null)
        {
            throw new InvalidOperationException(error?.LocalizedDescription ?? "iOS could not start recording.");
        }

        if (!recorder.PrepareToRecord() || !recorder.Record())
        {
            recorder.Dispose();
            throw new InvalidOperationException("iOS could not start recording.");
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

        var recorded = recorder.CurrentTime > 0.2;
        recorder.Stop();
        recorder.Dispose();
        // Вернуть сессию к воспроизведению: иначе звук может идти в разговорный динамик
        AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback);
        return recorded;
    }
}
