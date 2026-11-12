using DualSenseCompanion.Emulation;
using NAudio.CoreAudioApi;

namespace DualSenseCompanion.Features;

//Animated lightbar colors. All functions are pure so they can be tested and previewed.
public static class LightbarEffects
{
    //0.15..1 brightness, one full breath every 2 s at speed 1
    public static float Breathing(double seconds, float speed)
    {
        double phase = seconds * Math.PI * 2 * 0.5 * speed;
        return (float)(0.15 + 0.85 * (0.5 + 0.5 * Math.Sin(phase)));
    }

    //Full hue cycle every ~6.7 s at speed 1
    public static RgbColor Rainbow(double seconds, float speed)
    {
        double hue = (seconds * 0.15 * speed) % 1.0;
        return FromHue(hue);
    }

    //Brightness that follows the PC's audio level, never fully dark
    public static float AudioBrightness(float level) => 0.1f + 0.9f * Math.Min(1f, level * 1.6f);

    //Gentle breathing for the mic-muted reminder (0.35..1)
    public static float MicMutedPulse(double seconds) =>
        (float)(0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(seconds * Math.PI * 2 * 0.4)));

    public static RgbColor FromHue(double hue)
    {
        double h = (hue - Math.Floor(hue)) * 6;
        int sector = (int)h;
        double f = h - sector;
        byte up = (byte)Math.Round(255 * f);
        byte down = (byte)Math.Round(255 * (1 - f));

        return sector switch
        {
            0 => new RgbColor(255, up, 0),
            1 => new RgbColor(down, 255, 0),
            2 => new RgbColor(0, 255, up),
            3 => new RgbColor(0, down, 255),
            4 => new RgbColor(up, 0, 255),
            _ => new RgbColor(255, 0, down),
        };
    }
}

//Peak level of whatever Windows is playing on the default output device
public sealed class AudioLevelMeter : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private long _nextRefreshMs;
    private float _smoothed;

    //Call from one thread only. Returns 0..1, quick to rise and slow to fall so the light doesn't flicker.
    public float Read(long nowMs)
    {
        float peak = 0;

        try
        {
            //Follow changes of the default device (headphones plugged in, etc.)
            if (_device == null || nowMs >= _nextRefreshMs)
            {
                _enumerator ??= new MMDeviceEnumerator();
                _device?.Dispose();
                _device = _enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : null;
                _nextRefreshMs = nowMs + 5000;
            }

            peak = _device?.AudioMeterInformation.MasterPeakValue ?? 0;
        }
        catch
        {
            _device?.Dispose();
            _device = null;
        }

        _smoothed = Math.Max(peak, _smoothed * 0.85f);
        return _smoothed;
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator?.Dispose();
    }
}
