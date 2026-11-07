using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orknux.Desktop;

/// <summary>
/// The server and the token, kept in the person's own profile. The token is
/// encrypted with Windows' data protection for this user, so the file is
/// useless to anybody who copies it off the machine or reads it as somebody else.
/// </summary>
internal sealed class Settings
{
    public string Server { get; set; } = "";
    public string ProtectedToken { get; set; } = "";

    /**
     * Whether to connect again on the next start: set by a connection that
     * worked, cleared by the person disconnecting or pressing Ctrl+Alt+Q, so an
     * update or a restart picks up where it was and a deliberate stop stays one.
     */
    public bool Reconnect { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orknux", "desktop.json");

    public string Token
    {
        get
        {
            if (ProtectedToken.Length == 0) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                return "";
            }
        }
        set => ProtectedToken = value.Length == 0
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings() : new Settings();
        }
        catch (Exception)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
