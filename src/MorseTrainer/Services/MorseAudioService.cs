using System.IO;
using System.Text;
using MorseTrainer.Domain;

namespace MorseTrainer.Services;

public sealed record AudioClip(byte[] WavBytes, TimeSpan Duration);

public static class MorseAudioService
{
    private const int SampleRate = 44_100;
    private const short BitsPerSample = 16;
    private const short ChannelCount = 1;

    public static AudioClip Render(
        string groupedText,
        int charactersPerMinute,
        int frequencyHz,
        int volumePercent,
        int characterGapUnits,
        int groupGapUnits,
        bool playStartSignal = false,
        int startPauseUnits = 21)
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

        // Five characters form one standard PARIS word, so CPM = WPM × 5.
        var dotDurationSeconds = 6d / charactersPerMinute;
        using var samples = new MemoryStream();
        using (var writer = new BinaryWriter(samples, Encoding.ASCII, leaveOpen: true))
        {
            if (playStartSignal)
            {
                WriteGroupedText(writer, "ЖЖЖ", dotDurationSeconds, frequencyHz, volumePercent,
                    characterGapUnits, groupGapUnits);
                WriteSilence(writer, dotDurationSeconds * startPauseUnits);
            }

            WriteGroupedText(writer, groupedText, dotDurationSeconds, frequencyHz, volumePercent,
                characterGapUnits, groupGapUnits);
            WriteSilence(writer, dotDurationSeconds * 2);
        }

        var pcmBytes = samples.ToArray();
        var wavBytes = CreateWaveFile(pcmBytes);
        var durationSeconds = pcmBytes.Length / (double)(SampleRate * ChannelCount * (BitsPerSample / 8));
        return new AudioClip(wavBytes, TimeSpan.FromSeconds(durationSeconds));
    }

    private static void WriteGroupedText(
        BinaryWriter writer,
        string groupedText,
        double dotDurationSeconds,
        int frequencyHz,
        int volumePercent,
        int characterGapUnits,
        int groupGapUnits)
    {
        for (var index = 0; index < groupedText.Length; index++)
        {
            var symbol = groupedText[index];
            if (char.IsWhiteSpace(symbol))
            {
                continue;
            }

            if (!MorseAlphabet.TryGetCode(symbol, out var code))
            {
                continue;
            }

            for (var codeIndex = 0; codeIndex < code.Length; codeIndex++)
            {
                var units = code[codeIndex] == '.' ? 1 : 3;
                WriteTone(writer, dotDurationSeconds * units, frequencyHz, volumePercent);
                if (codeIndex < code.Length - 1)
                {
                    WriteSilence(writer, dotDurationSeconds);
                }
            }

            var nextIndex = index + 1;
            var isGroupBoundary = nextIndex < groupedText.Length && char.IsWhiteSpace(groupedText[nextIndex]);
            var isLastSymbol = nextIndex >= groupedText.Length;
            if (!isLastSymbol)
            {
                WriteSilence(writer, dotDurationSeconds * (isGroupBoundary ? groupGapUnits : characterGapUnits));
            }
        }
    }

    private static void WriteTone(BinaryWriter writer, double durationSeconds, int frequencyHz, int volumePercent)
    {
        var sampleCount = Math.Max(1, (int)Math.Round(SampleRate * durationSeconds));
        var rampSamples = Math.Min(sampleCount / 2, (int)(SampleRate * 0.005));
        var amplitude = short.MaxValue * 0.85 * (volumePercent / 100d);

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

            var phase = 2d * Math.PI * frequencyHz * sampleIndex / SampleRate;
            var sample = (short)Math.Round(Math.Sin(phase) * amplitude * Math.Max(0, envelope));
            writer.Write(sample);
        }
    }

    private static void WriteSilence(BinaryWriter writer, double durationSeconds)
    {
        var sampleCount = Math.Max(0, (int)Math.Round(SampleRate * durationSeconds));
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            writer.Write((short)0);
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
