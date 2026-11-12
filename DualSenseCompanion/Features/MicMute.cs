using NAudio.CoreAudioApi;

namespace DualSenseCompanion.Features;

//Mutes/unmutes every active Windows recording device
public static class MicMute
{
    //Mute state of the default recording device, or null if there is none
    public static bool? IsMuted()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read microphone state: {ex.Message}");
            return null;
        }
    }

    //Returns the new state
    public static bool Toggle()
    {
        bool target = !(IsMuted() ?? false);
        SetAll(target);
        return target;
    }

    public static void SetAll(bool muted)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    device.AudioEndpointVolume.Mute = muted;
                }
            }

            Console.WriteLine(muted ? "Microphone muted." : "Microphone unmuted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not change microphone state: {ex.Message}");
        }
    }
}
