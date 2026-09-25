using System.IO;
using System.Text;
using MorseTrainer.Domain;

namespace MorseTrainer.Services;

/// <summary>Звук задания; GroupStarts — когда начинается каждая группа (для «Группа 3 из 10» во время прослушивания).</summary>
public sealed record AudioClip(byte[] WavBytes, TimeSpan Duration, IReadOnlyList<TimeSpan>? GroupStarts = null)
{
    public int GroupCount => GroupStarts?.Count ?? 0;

    /// <summary>Номер группы (с 1), которая звучит к моменту elapsed; 0 — задание ещё не началось (идёт Ж Ж Ж).</summary>
    public int GroupAt(TimeSpan elapsed)
    {
        if (GroupStarts is null)
        {
            return 0;
        }

        var group = 0;
        while (group < GroupStarts.Count && GroupStarts[group] <= elapsed)
        {
            group++;
        }

        return group;
    }
}

/// <summary>
/// Помехи эфира: белый шум приёмника (QRN), медленные замирания (QSB) и уход частоты тона.
/// Все значения 0 — чистый сигнал.
/// </summary>
public sealed record NoiseProfile(int NoisePercent, int QsbPercent, int DriftHz)
{
    public const int MaxDriftHz = 50;

    public static readonly NoiseProfile None = new(0, 0, 0);

    public bool IsClean => NoisePercent <= 0 && QsbPercent <= 0 && DriftHz <= 0;

    public NoiseProfile Clamp() => new(
        Math.Clamp(NoisePercent, 0, 100),
        Math.Clamp(QsbPercent, 0, 100),
        Math.Clamp(DriftHz, 0, MaxDriftHz));
}

public static class MorseAudioService
{
    private const int SampleRate = 44_100;
    private const short BitsPerSample = 16;
    private const short ChannelCount = 1;

    // Период замираний и дрейфа: медленные, как в реальном эфире
    private const double QsbPeriodSeconds = 8;
    private const double DriftPeriodSeconds = 20;

    public static AudioClip Render(
        string groupedText,
        int charactersPerMinute,
        int frequencyHz,
        int volumePercent,
        int characterGapUnits,
        int groupGapUnits,
        bool playStartSignal = false,
        int startPauseUnits = 21,
        NoiseProfile? noise = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupedText);
        if (charactersPerMinute is < 20 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(charactersPerMinute));
        }

        if (frequencyHz is < 300 or > 1_200)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }

        if (volumePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volumePercent));
        }

        if (characterGapUnits is < 3 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(characterGapUnits));
        }

        if (groupGapUnits is < 7 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(groupGapUnits));
        }

        if (startPauseUnits is < 7 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(startPauseUnits));
        }

        var profile = (noise ?? NoiseProfile.None).Clamp();
        // Five characters form one standard PARIS word, so CPM = WPM × 5.
        var dotDurationSeconds = 6d / charactersPerMinute;
        var builder = new SignalBuilder(frequencyHz, profile.DriftHz);
        if (playStartSignal)
        {
            builder.WriteGroupedText("ЖЖЖ", dotDurationSeconds, characterGapUnits, groupGapUnits);
            builder.WriteSilence(dotDurationSeconds * startPauseUnits);
        }

        builder.GroupStarts = new List<TimeSpan>();
        builder.WriteGroupedText(groupedText, dotDurationSeconds, characterGapUnits, groupGapUnits);
        var groupStarts = builder.GroupStarts;
        builder.GroupStarts = null;
        builder.WriteSilence(dotDurationSeconds * 2);

        var pcmBytes = builder.ToPcm(volumePercent, profile);
        var wavBytes = CreateWaveFile(pcmBytes);
        var durationSeconds = pcmBytes.Length / (double)(SampleRate * ChannelCount * (BitsPerSample / 8));
        return new AudioClip(wavBytes, TimeSpan.FromSeconds(durationSeconds), groupStarts);
    }

    /// <summary>
    /// Непрерывный тон для ручного ключа: целое число периодов без огибающей, чтобы зацикленное
    /// воспроизведение не щёлкало на стыке.
    /// </summary>
    public static AudioClip RenderTone(double durationSeconds, int frequencyHz, int volumePercent)
    {
        if (frequencyHz is < 300 or > 1_200)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }

        if (volumePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volumePercent));
        }

        var periods = Math.Max(1, (int)Math.Round(Math.Max(0.05, durationSeconds) * frequencyHz));
        var seconds = periods / (double)frequencyHz;
        var sampleCount = Math.Max(1, (int)Math.Round(SampleRate * seconds));
        var amplitude = short.MaxValue * 0.85 * (volumePercent / 100d);
        using var samples = new MemoryStream();
        using (var writer = new BinaryWriter(samples, Encoding.ASCII, leaveOpen: true))
        {
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var phase = 2d * Math.PI * frequencyHz * sampleIndex / SampleRate;
                writer.Write((short)Math.Round(Math.Sin(phase) * amplitude));
            }
        }

        var pcmBytes = samples.ToArray();
        return new AudioClip(CreateWaveFile(pcmBytes), TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Сигнал в нормированных отсчётах (−1…1) с непрерывной фазой: дрейф частоты и замирания
    /// зависят от времени с начала записи, поэтому сначала собирается весь сигнал, потом добавляются помехи.
    /// </summary>
    private sealed class SignalBuilder
    {
        private readonly List<float> _samples = new(SampleRate * 30);

        /// <summary>Не null — отмечать момент начала каждой группы (сигнал Ж Ж Ж не отмечается).</summary>
        public List<TimeSpan>? GroupStarts;
        private readonly int _frequencyHz;
        private readonly int _driftHz;
        private double _phase;

        public SignalBuilder(int frequencyHz, int driftHz)
        {
            _frequencyHz = frequencyHz;
            _driftHz = driftHz;
        }

        public void WriteGroupedText(string groupedText, double dotDurationSeconds, int characterGapUnits, int groupGapUnits)
        {
            var groupStarts = true;
            for (var index = 0; index < groupedText.Length; index++)
            {
                var symbol = groupedText[index];
                if (char.IsWhiteSpace(symbol))
                {
                    groupStarts = true;
                    continue;
                }

                if (!MorseAlphabet.TryGetCode(symbol, out var code))
                {
                    continue;
                }

                if (groupStarts)
                {
                    GroupStarts?.Add(TimeSpan.FromSeconds(_samples.Count / (double)SampleRate));
                    groupStarts = false;
                }

                for (var codeIndex = 0; codeIndex < code.Length; codeIndex++)
                {
                    var units = code[codeIndex] == '.' ? 1 : 3;
                    WriteTone(dotDurationSeconds * units);
                    if (codeIndex < code.Length - 1)
                    {
                        WriteSilence(dotDurationSeconds);
                    }
                }

                var nextIndex = index + 1;
                var isGroupBoundary = nextIndex < groupedText.Length && char.IsWhiteSpace(groupedText[nextIndex]);
                var isLastSymbol = nextIndex >= groupedText.Length;
                if (!isLastSymbol)
                {
                    WriteSilence(dotDurationSeconds * (isGroupBoundary ? groupGapUnits : characterGapUnits));
                }
            }
        }

        public void WriteTone(double durationSeconds)
        {
            var sampleCount = Math.Max(1, (int)Math.Round(SampleRate * durationSeconds));
            var rampSamples = Math.Min(sampleCount / 2, (int)(SampleRate * 0.005));
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var envelope = 1d;
                if (rampSamples > 0 && sampleIndex < rampSamples)
                {
                    envelope = sampleIndex / (double)rampSamples;
                }
                else if (rampSamples > 0 && sampleIndex >= sampleCount - rampSamples)
                {
                    envelope = (sampleCount - sampleIndex - 1) / (double)rampSamples;
                }

                // Дрейф: частота медленно плавает вокруг заданной, фаза остаётся непрерывной
                var seconds = _samples.Count / (double)SampleRate;
                var frequency = _frequencyHz + (_driftHz == 0 ? 0 : _driftHz * Math.Sin(2d * Math.PI * seconds / DriftPeriodSeconds));
                _phase += 2d * Math.PI * frequency / SampleRate;
                _samples.Add((float)(Math.Sin(_phase) * Math.Max(0, envelope)));
            }
        }

        public void WriteSilence(double durationSeconds)
        {
            var sampleCount = Math.Max(0, (int)Math.Round(SampleRate * durationSeconds));
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                _samples.Add(0f);
            }
        }

        /// <summary>Громкость, замирания и шум → 16-битные отсчёты.</summary>
        public byte[] ToPcm(int volumePercent, NoiseProfile profile)
        {
            var amplitude = short.MaxValue * 0.85 * (volumePercent / 100d);
            var qsbDepth = profile.QsbPercent / 100d;
            // Шум привязан к громкости сигнала, чтобы соотношение сигнал/шум не зависело от ползунка громкости
            var noiseAmplitude = amplitude * 0.45 * (profile.NoisePercent / 100d);
            var random = new Random();
            var filtered = 0d;
            var pcm = new byte[_samples.Count * 2];
            for (var index = 0; index < _samples.Count; index++)
            {
                var seconds = index / (double)SampleRate;
                var fading = qsbDepth == 0
                    ? 1d
                    : 1d - qsbDepth * (0.5 + 0.5 * Math.Sin(2d * Math.PI * seconds / QsbPeriodSeconds));
                var value = _samples[index] * amplitude * fading;
                if (noiseAmplitude > 0)
                {
                    // Однополюсный фильтр смягчает белый шум до «шипения приёмника»
                    filtered += 0.2 * ((random.NextDouble() * 2 - 1) - filtered);
                    value += filtered * noiseAmplitude * 2.5;
                }

                var sample = (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
                pcm[index * 2] = (byte)(sample & 0xFF);
                pcm[index * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return pcm;
        }
    }

    private static byte[] CreateWaveFile(byte[] pcmBytes)
    {
        using var wave = new MemoryStream();
        using var writer = new BinaryWriter(wave, Encoding.ASCII, leaveOpen: true);
        var byteRate = SampleRate * ChannelCount * BitsPerSample / 8;
        var blockAlign = (short)(ChannelCount * BitsPerSample / 8);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcmBytes.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(ChannelCount);
        writer.Write(SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(BitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcmBytes.Length);
        writer.Write(pcmBytes);
        writer.Flush();
        return wave.ToArray();
    }
}
