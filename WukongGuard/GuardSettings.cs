using System;
using System.Collections.Generic;
using System.IO;
using WukongGuard.Core;

namespace WukongGuard
{
    internal static class GuardSettings
    {
        private static readonly string PathToSettings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WukongGuard", "settings.json");
        private static DateTime nextReadUtc;
        private static bool hiddenAreaHints;
        private static bool loggedError;

        internal static bool HiddenAreaHints()
        {
            if (DateTime.UtcNow < nextReadUtc) return hiddenAreaHints;
            nextReadUtc = DateTime.UtcNow.AddSeconds(3);
            try
            {
                if (!File.Exists(PathToSettings))
                {
                    hiddenAreaHints = false;
                    return false;
                }
                var root = StrictJson.Parse(File.ReadAllText(PathToSettings)) as Dictionary<string, object>;
                if (root == null || !root.TryGetValue("HiddenAreaHints", out var value)
                    || !(value is bool enabled))
                    throw new FormatException("HiddenAreaHints must be boolean");
                hiddenAreaHints = enabled;
                loggedError = false;
            }
            catch (Exception ex)
            {
                hiddenAreaHints = false;
                if (!loggedError)
                {
                    loggedError = true;
                    TraceLog.Write("[WukongGuard] settings invalid; hidden area hints disabled: " + ex);
                }
            }
            return hiddenAreaHints;
        }
    }
}
