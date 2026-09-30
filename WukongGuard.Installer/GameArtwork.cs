using Microsoft.Win32;

namespace WukongGuard.Installer;

internal static class GameArtwork
{
    // Artwork belongs to Game Science. Use the player's existing Steam artwork;
    // the public executable and source repository do not redistribute it.
    internal static Image? Load(string? gameRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            Add(user?.GetValue("SteamPath") as string);
            Add(machine?.GetValue("InstallPath") as string);
            if (!string.IsNullOrWhiteSpace(gameRoot))
                Add(Directory.GetParent(gameRoot)?.Parent?.Parent?.FullName);
            foreach (var drive in Environment.GetLogicalDrives())
                foreach (var suffix in new[] { "Steam", @"Program Files (x86)\Steam", @"Program Files\Steam" })
                    Add(Path.Combine(drive, suffix));
            foreach (var root in roots)
            {
                var cache = Path.Combine(root, "appcache", "librarycache");
                foreach (var name in new[] { @"2358720\library_hero.jpg", "2358720_library_hero.jpg",
                    @"2358720\library_header_schinese.jpg", "2358720_library_header.jpg" })
                {
                    var file = Path.Combine(cache, name);
                    if (!File.Exists(file)) continue;
                    try
                    {
                        using var original = Image.FromFile(file);
                        return new Bitmap(original);
                    }
                    catch (ArgumentException) { }
                    catch (IOException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) roots.Add(path);
        }
    }
}
