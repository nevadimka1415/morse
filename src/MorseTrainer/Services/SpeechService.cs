using System.Runtime.InteropServices;

namespace MorseTrainer.Services;

public static class SpeechService
{
    public static Task<bool> SpeakAsync(string chant)
    {
        return Task.Run(() =>
        {
            object? voice = null;
            try
            {
                var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType is null)
                {
                    return false;
                }

                voice = Activator.CreateInstance(voiceType);
                if (voice is null)
                {
                    return false;
                }

                dynamic speaker = voice;
                speaker.Rate = -2;
                speaker.Volume = 100;
                speaker.Speak(chant.Replace('-', ' '), 0);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (voice is not null && Marshal.IsComObject(voice))
                {
                    Marshal.FinalReleaseComObject(voice);
                }
            }
        });
    }
}
