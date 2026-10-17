// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: where the owner key (derived from the 12-word phrase) lives on the server.
// The phrase is NOT stored: only the 32-byte key derived from it, in an owner-only file or in
// ZEROGRAM_OWNER_KEY (64 hex characters).

using System.Security.Cryptography;

namespace Ferrite.Services.Zerogram;

public static class ZgOwnerKeyFile
{
    public static string PathFor(string dataPath) => Path.Combine(dataPath, "owner.key");

    public static byte[]? Load(string dataPath, string? fromEnvironmentHex)
    {
        try
        {
            string? hex = !string.IsNullOrWhiteSpace(fromEnvironmentHex) ? fromEnvironmentHex.Trim()
                : File.Exists(PathFor(dataPath)) ? File.ReadAllText(PathFor(dataPath)).Trim() : null;
            if (hex is null || hex.Length != 64) return null;
            return Convert.FromHexString(hex);
        }
        catch { return null; }
    }

    /// <summary>Writes the key file (0600). Returns false when one exists and force is not set.</summary>
    public static bool Save(string dataPath, byte[] key, bool force)
    {
        string path = PathFor(dataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && !force) return false;
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using var stream = new FileStream(path, options);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(key).ToLowerInvariant() + "\n"));
        stream.Flush(true);
        return true;
    }
}
