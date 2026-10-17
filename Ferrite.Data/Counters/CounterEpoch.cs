// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: crash-safe issued identifiers.
//
// FASTER checkpoints the counters that hand out message ids, pts, seq and user/chat ids about every
// 100 ms. After a hard stop (kill -9, power loss) the most recent increments can be missing, which would
// let the server re-issue an id or move pts *backwards* relative to what a client already holds.
// A "running" marker detects that the previous run did not shut down cleanly; each unclean start then
// permanently raises an epoch offset added to every issued value, jumping past anything that could have
// been handed out but not checkpointed. Clean stops leave the offset unchanged.

namespace Ferrite.Data.Counters;

public static class CounterEpoch
{
    public const long UncleanStartBump = 500_000;

    private static string? _runningPath;

    public static long Offset { get; private set; }
    public static bool LastRunWasClean { get; private set; } = true;

    public static void Initialize(string dataPath, long bump = UncleanStartBump)
    {
        Directory.CreateDirectory(dataPath);
        string epochPath = Path.Combine(dataPath, "zerogram-epoch");
        _runningPath = Path.Combine(dataPath, "zerogram-running");

        long offset = 0;
        if (File.Exists(epochPath) && long.TryParse(File.ReadAllText(epochPath).Trim(), out long stored))
            offset = stored;

        LastRunWasClean = !File.Exists(_runningPath);
        if (!LastRunWasClean)
        {
            offset += bump;
            WriteDurable(epochPath, offset.ToString());
        }
        else if (!File.Exists(epochPath))
        {
            WriteDurable(epochPath, offset.ToString());
        }
        WriteDurable(_runningPath, DateTimeOffset.UtcNow.ToString("O"));
        Offset = offset;
    }

    public static void MarkCleanShutdown()
    {
        if (_runningPath is not null && File.Exists(_runningPath)) File.Delete(_runningPath);
    }

    private static void WriteDurable(string path, string text)
    {
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(System.Text.Encoding.ASCII.GetBytes(text));
            stream.Flush(true);
        }
        File.Move(temp, path, overwrite: true);
    }
}
