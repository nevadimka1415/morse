using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MorseTrainer.Services;

/// <summary>
/// Запись с микрофона по умолчанию через MCI (winmm.dll) — без дополнительных библиотек, чтобы не раздувать установщик.
/// Формат — PCM 16 бит, 22,05 кГц, моно, как встроенный голос Windows-версии.
/// </summary>
public sealed class VoiceRecorder : IDisposable
{
    private const string Alias = "morsetrainervoice";

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendStringW(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorStringW(int error, StringBuilder text, int length);

    public bool IsRecording { get; private set; }

    public void Start()
    {
        Cancel();
        Send($"open new type waveaudio alias {Alias}");
        try
        {
            Send($"set {Alias} time format ms bitspersample 16 channels 1 samplespersec 22050 bytespersec 44100 alignment 2");
            Send($"record {Alias}");
        }
        catch
        {
            Close();
            throw;
        }

        IsRecording = true;
    }

    /// <summary>Останавливает запись и сохраняет её в WAV (через временный файл: оборванная запись не заменит прежнюю).</summary>
    public void StopAndSave(string path)
    {
        if (!IsRecording)
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".part.wav";
        try
        {
            Send($"stop {Alias}");
            Send($"save {Alias} \"{temp}\"");
        }
        finally
        {
            Close();
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Прервать запись без сохранения.</summary>
    public void Cancel()
    {
        if (IsRecording)
        {
            Close();
        }
    }

    public void Dispose() => Cancel();

    private void Close()
    {
        mciSendStringW($"close {Alias}", null, 0, IntPtr.Zero);
        IsRecording = false;
    }

    private static void Send(string command)
    {
        var error = mciSendStringW(command, null, 0, IntPtr.Zero);
        if (error == 0)
        {
            return;
        }

        var text = new StringBuilder(256);
        var message = mciGetErrorStringW(error, text, text.Capacity) ? text.ToString() : $"MCI {error}";
        throw new InvalidOperationException(message);
    }
}
