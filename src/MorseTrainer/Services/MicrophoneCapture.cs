using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MorseTrainer.Services;

/// <summary>
/// Микрофон по умолчанию через waveIn (winmm.dll) — без сторонних библиотек, чтобы не раздувать установщик:
/// 22 050 Гц, моно, 16 бит. Четыре буфера по 50 мс опрашиваются из фонового потока — функции waveIn нельзя вызывать
/// из их собственного обратного вызова. Отсчёты −1…1 отдаются в onSamples из этого потока.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    public const int SampleRate = 22_050;
    private const int BufferCount = 4;
    private const int BufferSamples = SampleRate / 20;
    private const int WaveMapper = -1;
    private const int HeaderDone = 1;
    private const int HeaderPrepared = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSecond;
        public int AverageBytesPerSecond;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr handle, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr handle);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveInGetErrorTextW(int error, StringBuilder text, int length);

    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private IntPtr _handle;
    private Thread? _thread;
    private volatile bool _running;
    private Action<float[], int>? _onSamples;

    public bool IsRunning => _running;

    public void Start(Action<float[], int> onSamples)
    {
        ArgumentNullException.ThrowIfNull(onSamples);
        Stop();
        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSecond = SampleRate,
            AverageBytesPerSecond = SampleRate * 2,
            BlockAlign = 2,
            BitsPerSample = 16
        };
        var opened = waveInOpen(out _handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (opened != 0)
        {
            _handle = IntPtr.Zero;
            throw new InvalidOperationException(ErrorText(opened));
        }

        var headerSize = Marshal.SizeOf<WaveHeader>();
        try
        {
            for (var index = 0; index < BufferCount; index++)
            {
                _buffers[index] = Marshal.AllocHGlobal(BufferSamples * 2);
                _headers[index] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = _buffers[index], BufferLength = BufferSamples * 2 }, _headers[index], false);
                Check(waveInPrepareHeader(_handle, _headers[index], headerSize));
                Check(waveInAddBuffer(_handle, _headers[index], headerSize));
            }

            Check(waveInStart(_handle));
        }
        catch
        {
            Stop();
            throw;
        }

        _onSamples = onSamples;
        _running = true;
        _thread = new Thread(Poll) { IsBackground = true, Name = "Morse microphone" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
        var headerSize = Marshal.SizeOf<WaveHeader>();
        if (_handle != IntPtr.Zero)
        {
            // Reset возвращает все буферы — только потом их можно снять с подготовки и освободить
            waveInReset(_handle);
            foreach (var header in _headers)
            {
                if (header != IntPtr.Zero)
                {
                    waveInUnprepareHeader(_handle, header, headerSize);
                }
            }

            waveInClose(_handle);
            _handle = IntPtr.Zero;
        }

        for (var index = 0; index < BufferCount; index++)
        {
            if (_headers[index] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headers[index]);
                _headers[index] = IntPtr.Zero;
            }

            if (_buffers[index] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffers[index]);
                _buffers[index] = IntPtr.Zero;
            }
        }
    }

    public void Dispose() => Stop();

    private void Poll()
    {
        var headerSize = Marshal.SizeOf<WaveHeader>();
        var flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
        var recordedOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.BytesRecorded));
        var shorts = new short[BufferSamples];
        var floats = new float[BufferSamples];
        var next = 0;
        while (_running)
        {
            var header = _headers[next];
            if ((Marshal.ReadInt32(header, flagsOffset) & HeaderDone) == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            var count = Math.Min(BufferSamples, Marshal.ReadInt32(header, recordedOffset) / 2);
            Marshal.Copy(_buffers[next], shorts, 0, count);
            for (var index = 0; index < count; index++)
            {
                floats[index] = shorts[index] / 32768f;
            }

            if (count > 0)
            {
                _onSamples?.Invoke(floats, count);
            }

            // Буфер — обратно в очередь записи
            Marshal.WriteInt32(header, flagsOffset, HeaderPrepared);
            Marshal.WriteInt32(header, recordedOffset, 0);
            if (_running)
            {
                waveInAddBuffer(_handle, header, headerSize);
            }

            next = (next + 1) % BufferCount;
        }
    }

    private static void Check(int result)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(ErrorText(result));
        }
    }

    private static string ErrorText(int error)
    {
        var text = new StringBuilder(256);
        return waveInGetErrorTextW(error, text, text.Capacity) == 0 ? text.ToString().TrimEnd('.', ' ') : $"waveIn {error}";
    }
}
