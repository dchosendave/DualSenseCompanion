using System.Text.Json;

namespace DualSenseCompanion.App;

//What this app changed in HidHide, written while the controller is hidden.
//If the app is killed or crashes hard, the next start reads it and undoes the changes.
public sealed record HiddenDeviceRecord(bool HidHideWasActive, List<string> InstanceIds);

public sealed class HiddenDeviceStore
{
    private readonly string _path;

    public HiddenDeviceStore(string path)
    {
        _path = path;
    }

    public static HiddenDeviceStore Default { get; } =
        new HiddenDeviceStore(Path.Combine(Settings.Directory, "hidden-devices.json"));

    public HiddenDeviceRecord? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var record = JsonSerializer.Deserialize<HiddenDeviceRecord>(File.ReadAllText(_path));
            return record is { InstanceIds.Count: > 0 } ? record : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read {_path}: {ex.Message}");
            return null;
        }
    }

    public void Save(HiddenDeviceRecord record)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            //Write then rename so a crash mid-write never leaves a broken file
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write {_path}: {ex.Message}");
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not delete {_path}: {ex.Message}");
        }
    }
}
