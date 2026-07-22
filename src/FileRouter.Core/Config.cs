using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileRouter.Core;

/// <summary>A single filing destination.</summary>
public sealed class Route
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("hotkey")] public string Hotkey { get; set; } = "";
    [JsonPropertyName("append_suffix")] public bool AppendSuffix { get; set; }
    [JsonPropertyName("suffix")] public string Suffix { get; set; } = "";
    [JsonPropertyName("naming_mode")] public string? NamingMode { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }

    // Python parity: hand-edited per-route keys survive a load/save round trip
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement> Extras { get; set; } = new();
}

/// <summary>A folder shown as a tile on the Ready dashboard while it holds
/// files — another process's work queue, a "failed" folder, etc.</summary>
public sealed class WatchFolder
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("recursive")] public bool Recursive { get; set; }
    [JsonPropertyName("filetypes")] public string Filetypes { get; set; } = "";   // "pdf" or "pdf,txt"; blank = any
    [JsonPropertyName("color")] public string? Color { get; set; }

    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement> Extras { get; set; } = new();
}

/// <summary>A saved Unlock-tool password. The password value is either
/// DPAPI-protected ("dpapi:&lt;base64&gt;", written by the app) or legacy
/// plaintext (hand-edited / migrated from the Python config).</summary>
public sealed class SavedPassword
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
}

/// <summary>config.json load/save/defaults/validation. Unknown top-level keys
/// survive a load/save round trip (kept in <see cref="Extras"/>).</summary>
public sealed class Config
{
    public static readonly string[] Sorts =
    {
        "filename_asc", "filename_desc", "mtime_asc", "mtime_desc",
        "size_asc", "size_desc",
    };

    [JsonPropertyName("inbox")] public string Inbox { get; set; } = "";
    [JsonPropertyName("deferred")] public string Deferred { get; set; } = "";
    [JsonPropertyName("names_file")] public string NamesFile { get; set; } = "names.txt";
    [JsonPropertyName("history_db")] public string HistoryDb { get; set; } = "history.sqlite";
    [JsonPropertyName("tag_with_route")] public bool TagWithRoute { get; set; } = true;
    [JsonPropertyName("naming_mode")] public string NamingMode { get; set; } = "insert";
    [JsonPropertyName("sort")] public string Sort { get; set; } = "size_desc";
    [JsonPropertyName("enter_commits")] public bool EnterCommits { get; set; } = true;
    [JsonPropertyName("uppercase_names")] public bool UppercaseNames { get; set; } = true;
    [JsonPropertyName("routes")] public List<Route> Routes { get; set; } = new();

    // Match & merge: which spreadsheet headers hold the names + the id
    [JsonPropertyName("merge_headers")] public Dictionary<string, string> MergeHeaders { get; set; } = new();

    // Ready dashboard: monitored-folder tiles + filename alerts
    [JsonPropertyName("watch_folders")] public List<WatchFolder> WatchFolders { get; set; } = new();
    [JsonPropertyName("alert_texts")] public List<string> AlertTexts { get; set; } = new();
    [JsonPropertyName("monitor_title")] public string MonitorTitle { get; set; } = "Monitored folders";
    [JsonPropertyName("flash_alerts")] public bool FlashAlerts { get; set; } = true;

    // Appearance (Python parity: same key names, so an old config round-trips)
    [JsonPropertyName("ui_font_family")] public string UiFontFamily { get; set; } = "";
    [JsonPropertyName("ui_font_size")] public int UiFontSize { get; set; }   // 0 = default

    // "auto" follows the Windows light/dark preference; "light"/"dark" force it
    [JsonPropertyName("theme")] public string Theme { get; set; } = "auto";

    // Typed space becomes this string in the name box ("" = keep spaces)
    [JsonPropertyName("word_separator")] public string WordSeparator { get; set; } = "";

    // Unlock tool: "" = decrypt the original in place; else suffix for a copy
    [JsonPropertyName("unlock_suffix")] public string UnlockSuffix { get; set; } = "";
    [JsonPropertyName("saved_passwords")] public List<SavedPassword> SavedPasswords { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement> Extras { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Load config, creating it with defaults on first run.</summary>
    public static Config Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new Config();
            Save(fresh, path);
            return fresh;
        }
        Config cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Opts)
                  ?? throw new ConfigException($"Config file {path} is empty");
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"Config file {path} is not valid JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Config file {path} could not be read: {ex.Message}");
        }
        if (Array.IndexOf(Naming.Modes, cfg.NamingMode) < 0)
            throw new ConfigException(
                $"naming_mode must be one of insert/replace, got \"{cfg.NamingMode}\"");
        if (Array.IndexOf(Sorts, cfg.Sort) < 0)
            throw new ConfigException($"sort must be one of {string.Join('/', Sorts)}, " +
                                      $"got \"{cfg.Sort}\"");
        if (cfg.UiFontSize is not 0 and (< 6 or > 72))
            throw new ConfigException(
                $"ui_font_size must be 0 (default) or 6-72, got {cfg.UiFontSize}");
        if (cfg.WordSeparator.Contains(' '))
            throw new ConfigException(
                "word_separator must not contain a space — substitution would loop forever");
        if (cfg.Theme is not ("auto" or "light" or "dark"))
            throw new ConfigException(
                $"theme must be one of auto/light/dark, got \"{cfg.Theme}\"");
        return cfg;
    }

    public static void Save(Config cfg, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Opts) + "\n");

    /// <summary>Save that reports failure instead of crashing the app — a
    /// read-only or locked config file must never take the session down.</summary>
    public static bool TrySave(Config cfg, string path, out string error)
    {
        try { Save(cfg, path); error = ""; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException
                                   or DirectoryNotFoundException)
        {
            error = $"Couldn't save settings to {path}: {ex.Message}";
            return false;
        }
    }

    /// <summary>Readable error for one unusable destination, or "" if good.</summary>
    public static string ValidateRoute(Route route)
    {
        var raw = route.Path?.Trim() ?? "";
        if (raw.Length == 0) return "no destination path configured";
        if (!Directory.Exists(raw))
            return File.Exists(raw)
                ? $"destination is not a folder: {raw}"
                : $"destination does not exist: {raw}";
        return ProbeWritable(raw);
    }

    /// <summary>Empty string if we can create files in dest, else a readable
    /// error. Actually creates and removes a probe file — os.access lies on
    /// Windows and over SMB.</summary>
    public static string ProbeWritable(string dest)
    {
        var probe = System.IO.Path.Combine(dest, $".filerouter_probe_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"destination not writable: {ex.Message}";
        }
    }
}

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}
