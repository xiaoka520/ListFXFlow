using System.Text.Json;

namespace ListFXFlow;

public sealed record ConnectionPreferences(string MacAddress="",string LocalAddress="",string LastMacAddress="",string AdapterName="",bool TouchpadEnabled=false,bool AutomaticGameProtection=true,bool ForwardMacGestures=false)
{
    private static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ListFXFlow","connection.json");
    public static ConnectionPreferences Load() {
        try { return JsonSerializer.Deserialize<ConnectionPreferences>(File.ReadAllText(SettingsPath))??new(); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save() {
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath,JsonSerializer.Serialize(this)); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { /* Connection remains usable if preferences cannot be saved. */ }
    }
}
