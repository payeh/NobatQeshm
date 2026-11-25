using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NobatQeshm.Wpf;

public static class AppPaths
{
    public static string DataDirectory { get; } = CreateDataDirectory();
    public static string Sessions => Path.Combine(DataDirectory, "saved_sessions.json");
    public static string Plates => Path.Combine(DataDirectory, "saved_plates.json");
    public static string Advertising => Path.Combine(DataDirectory, "receipt_advertising.json");
    private static string CreateDataDirectory()
    {
        // Same directory as the Python version: existing accounts and plates remain available.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QeshmNobatGUI");
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class SessionStore
{
    private static JsonObject Empty() => new() { ["version"] = 1, ["last"] = "", ["profiles"] = new JsonObject() };
    private JsonObject Read()
    {
        try { return JsonNode.Parse(File.ReadAllText(AppPaths.Sessions, Encoding.UTF8)) as JsonObject ?? Empty(); }
        catch { return Empty(); }
    }
    private static void Write(JsonObject data)
    {
        var temp = AppPaths.Sessions + ".tmp";
        File.WriteAllText(temp, data.ToJsonString(JsonHelpers.Options), new UTF8Encoding(false));
        File.Move(temp, AppPaths.Sessions, true);
    }
    public List<SavedProfile> Profiles()
    {
        var profiles = Read()["profiles"] as JsonObject ?? [];
        return profiles.Select(pair => ParseProfile(pair.Key, pair.Value as JsonObject)).Where(x => x is not null).Cast<SavedProfile>().OrderBy(x => x.Name).ToList();
    }
    public SavedProfile? Get(string nationalCode)
    {
        var row = (Read()["profiles"] as JsonObject)?[nationalCode] as JsonObject;
        return ParseProfile(nationalCode, row);
    }
    public string LastCode() => Read().Text("last");
    private static SavedProfile? ParseProfile(string code, JsonObject? row) => row is null ? null : new(
        row.TextOr("کاربر", "name"), row.TextOr(code, "national_code"), row.Text("mobile"), row.Text("expires"),
        row.Bool("expired"), row.Text("saved_at"), row["cookies"] as JsonObject ?? []);
    public void Save(string name, string nationalCode, string mobile, string expires, IEnumerable<CookieInfo> cookies, bool expired = false)
    {
        var list = cookies.ToList();
        if (string.IsNullOrWhiteSpace(nationalCode) || list.Count == 0) return;
        var raw = JsonSerializer.SerializeToUtf8Bytes(list, JsonHelpers.Options);
        var encrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        var data = Read();
        data["last"] = nationalCode;
        var profiles = data["profiles"] as JsonObject ?? [];
        profiles[nationalCode] = new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(name) ? "کاربر" : name.Trim(), ["national_code"] = nationalCode,
            ["mobile"] = mobile.Trim(), ["expires"] = expires, ["expired"] = expired,
            ["cookies"] = new JsonObject { ["scheme"] = "dpapi", ["data"] = Convert.ToBase64String(encrypted) },
            ["saved_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
        data["profiles"] = profiles;
        Write(data);
    }
    public List<CookieInfo> LoadCookies(string nationalCode)
    {
        var payload = Get(nationalCode)?.Cookies;
        if (payload is null) return [];
        try
        {
            var bytes = Convert.FromBase64String(payload.Text("data"));
            var raw = payload.Text("scheme") == "dpapi" ? ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser) : bytes;
            return JsonSerializer.Deserialize<List<CookieInfo>>(raw, JsonHelpers.Options) ?? [];
        }
        catch { return []; }
    }
    public void MarkExpired(string nationalCode, bool expired = true)
    {
        var data = Read();
        if ((data["profiles"] as JsonObject)?[nationalCode] is JsonObject row) { row["expired"] = expired; Write(data); }
    }
    public void Delete(string nationalCode)
    {
        var data = Read();
        var profiles = data["profiles"] as JsonObject ?? [];
        profiles.Remove(nationalCode);
        if (data.Text("last") == nationalCode) data["last"] = profiles.FirstOrDefault().Key ?? "";
        data["profiles"] = profiles;
        Write(data);
    }
}

public sealed class SavedPlateStore
{
    public List<SavedPlate> Load()
    {
        try { return JsonSerializer.Deserialize<List<SavedPlate>>(File.ReadAllText(AppPaths.Plates), JsonHelpers.Options) ?? []; }
        catch (FileNotFoundException) { return []; }
    }
    public void Write(IEnumerable<SavedPlate> plates)
    {
        var temp = AppPaths.Plates + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(plates, JsonHelpers.Options), new UTF8Encoding(false));
        File.Move(temp, AppPaths.Plates, true);
    }
    public List<SavedPlate> Save(SavedPlate plate, SavedPlate? original = null)
    {
        var plates = Load();
        if (original is not null && !plates.Contains(original)) throw new InvalidOperationException("این پلاک تغییر کرده است. فهرست را دوباره باز کنید.");
        if (plates.Any(x => x != original && x.TransferCode == plate.TransferCode && x.Number == plate.Number && x.Series == plate.Series))
            throw new InvalidOperationException("این مشخصات قبلاً برای پلاک دیگری ذخیره شده است.");
        if (original is null) plates.Add(plate); else plates[plates.IndexOf(original)] = plate;
        Write(plates);
        return plates;
    }
    public List<SavedPlate> Delete(SavedPlate plate)
    {
        var plates = Load();
        if (!plates.Remove(plate)) throw new InvalidOperationException("پلاک قبلاً تغییر کرده یا حذف شده است.");
        Write(plates);
        return plates;
    }
}

public static class AdvertisingStore
{
    public static ReceiptAdvertising Load()
    {
        try { return JsonSerializer.Deserialize<ReceiptAdvertising>(File.ReadAllText(AppPaths.Advertising), JsonHelpers.Options) ?? new(); }
        catch { return new(); }
    }
    public static void Save(ReceiptAdvertising value) => File.WriteAllText(AppPaths.Advertising, JsonSerializer.Serialize(value, JsonHelpers.Options), new UTF8Encoding(false));
}
