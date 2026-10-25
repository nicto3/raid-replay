using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RaidReplay.Core
{
    /// <summary>Folder of replays: naming, listing, pinning and "keep last N" retention.</summary>
    public sealed class ReplayLibrary
    {
        public string Directory { get; }
        public ReplayLibrary(string directory) { Directory = directory; System.IO.Directory.CreateDirectory(directory); }

        public string CreatePath(string map, DateTime localNow)
        {
            string safe = new string((map ?? "unknown").Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            return System.IO.Path.Combine(Directory, localNow.ToString("yyyyMMdd_HHmmss") + "_" + safe + ReplayFile.Extension);
        }

        string[] Files() => System.IO.Directory.GetFiles(Directory, "*" + ReplayFile.Extension)
                                .OrderByDescending(f => System.IO.Path.GetFileName(f), StringComparer.Ordinal).ToArray();

        public bool IsPinned(string path) => File.Exists(path + ".pin");
        public void SetPinned(string path, bool pinned)
        {
            if (pinned) File.WriteAllText(path + ".pin", "1"); else if (File.Exists(path + ".pin")) File.Delete(path + ".pin");
        }

        public List<ReplayInfo> List()
        {
            var list = new List<ReplayInfo>();
            foreach (var f in Files())
            {
                try { var i = ReplayFile.ReadInfo(f); i.Pinned = IsPinned(f); list.Add(i); }
                catch (Exception) { /* corrupt or foreign file: skip */ }
            }
            return list;
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".pin")) File.Delete(path + ".pin");
        }

        /// <summary>Keeps the newest <paramref name="keep"/> unpinned replays (pinned ones are never deleted or counted).</summary>
        public int ApplyRetention(int keep, string protectedPath = null)
        {
            keep = Math.Max(1, keep);
            int kept = 0, deleted = 0;
            foreach (var f in Files())
            {
                if (IsPinned(f)) continue;
                bool isProtected = protectedPath != null &&
                    string.Equals(System.IO.Path.GetFullPath(f), System.IO.Path.GetFullPath(protectedPath), StringComparison.OrdinalIgnoreCase);
                if (kept < keep || isProtected) { kept++; continue; }
                try { Delete(f); deleted++; } catch (IOException) { }
            }
            return deleted;
        }
    }
}
