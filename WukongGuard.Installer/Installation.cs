using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Principal;
using Microsoft.Win32;

namespace WukongGuard.Installer;

internal static class Installation
{
    internal const string ProductVersion = "0.5.0-rc1";
    private const string LoaderUrl =
        "https://github.com/czastack/B1CSharpLoader/releases/download/v0.0.8/B1CSharpLoader-0.0.8.zip";
    private const string LoaderSha256 =
        "721E8C34174060AD988B91100A63D013AF361109E2A173A754C7FEDA1847D4F0";
    private const string PayloadResource = "WukongGuardPayload.zip";
    private const long MaxLoaderBytes = 30L * 1024 * 1024;
    private static readonly Lazy<string> EmbeddedPayloadHash = new(() =>
    {
        using var resource = typeof(Installation).Assembly.GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("程序缺少内置游戏组件。");
        return Convert.ToHexString(SHA256.HashData(resource));
    });

    internal static string? TryFindGameRoot()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(steamRoots, Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string);
        AddIfPresent(steamRoots, Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") as string);
        foreach (var drive in Environment.GetLogicalDrives())
            foreach (var suffix in new[] { "Steam", "SteamLibrary", @"Program Files (x86)\Steam", @"Program Files\Steam" })
                AddIfPresent(steamRoots, Path.Combine(drive, suffix));

        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var root in steamRoots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (var line in File.ReadLines(vdf))
            {
                var match = Regex.Match(line, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (match.Success)
                    AddIfPresent(libraries, match.Groups[1].Value.Replace("\\\\", "\\"));
            }
        }

        var matches = libraries.Select(root => Path.Combine(root, "steamapps", "common", "BlackMythWukong"))
            .Where(IsGameRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void AddIfPresent(HashSet<string> set, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full)) set.Add(full);
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
    }

    internal static bool IsGameRoot(string path) => File.Exists(Path.Combine(path,
        "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"));

    internal static bool NeedsPreparation(string gameRoot)
    {
        if (!IsGameRoot(gameRoot)) return true;
        var modDir = SessionControl.ModDirectory(gameRoot);
        var marker = Path.Combine(modDir, ".product-version.txt");
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != ProductVersion) return true;
        var packageHash = Path.Combine(modDir, ".package-sha256.txt");
        if (!File.Exists(packageHash)
            || !File.ReadAllText(packageHash).Trim().Equals(EmbeddedPayloadHash.Value,
                StringComparison.OrdinalIgnoreCase)) return true;
        if (!SessionControl.IsInstalled(gameRoot)
            || !File.Exists(Path.Combine(modDir, "rules.json"))
            || !File.Exists(Path.Combine(modDir, "Overlay", "WukongGuard.Overlay.exe"))
            || !File.Exists(Path.Combine(gameRoot, "b1", "Binaries", "Win64", "version.dll")))
            return true;
        var ini = Path.Combine(gameRoot, "b1", "Binaries", "Win64", "CSharpLoader", "b1cs.ini");
        return !File.Exists(ini) || !HasSetting(ini, "Console", "0")
            || !HasSetting(ini, "EnableJit", "0");
    }

    private static bool HasSetting(string path, string name, string expected)
    {
        var pattern = new Regex(@"^[ \t]*" + Regex.Escape(name) + @"[ \t]*=[ \t]*(.*?)[ \t]*$",
            RegexOptions.IgnoreCase);
        var values = File.ReadLines(path).Select(line => pattern.Match(line))
            .Where(match => match.Success).Select(match => match.Groups[1].Value).ToArray();
        return values.Length == 1 && values[0] == expected;
    }

    internal static void VerifyPayload()
    {
        var temp = CreateTemporaryDirectory();
        try { ExtractAndVerifyPayload(temp); }
        finally { DeleteTemporaryDirectory(temp); }
    }

    internal static Task InstallAsync(string gameRoot, Action<string> progress, string? sessionUserSid = null) =>
        Task.Run(async () => await InstallCoreAsync(gameRoot, progress, sessionUserSid));

    private static async Task InstallCoreAsync(string gameRoot, Action<string> progress,
        string? sessionUserSid)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new InvalidOperationException("请先选择游戏安装目录。");
        gameRoot = Path.GetFullPath(gameRoot);
        if (!IsGameRoot(gameRoot))
            throw new InvalidOperationException("所选目录中未找到《黑神话：悟空》游戏程序。请选择 BlackMythWukong 文件夹。");
        if (Process.GetProcessesByName("b1-Win64-Shipping").Length != 0)
            throw new InvalidOperationException("请先完全退出《黑神话：悟空》，然后重新安装。");
        if (Process.GetProcessesByName("WukongGuard.Overlay").Length != 0)
            throw new InvalidOperationException("请先退出正在运行的提醒窗口，然后重新准备。");

        var temp = CreateTemporaryDirectory();
        try
        {
            progress("校验安装包…");
            var packageRoot = ExtractAndVerifyPayload(temp);
            var gameBin = Path.Combine(gameRoot, "b1", "Binaries", "Win64");
            await EnsureLoaderAsync(gameBin, temp, progress);
            ConfigureLoader(gameBin);
            progress("备份旧版并准备后悔药…");
            InstallGuard(packageRoot, gameBin);
            GrantSessionAccess(SessionControl.ModDirectory(gameRoot), sessionUserSid);
            Log("Install succeeded. Game root: " + gameRoot);
            progress("准备完成。");
        }
        finally { DeleteTemporaryDirectory(temp); }
    }

    private static string ExtractAndVerifyPayload(string temp)
    {
        var zip = Path.Combine(temp, "payload.zip");
        using (var resource = typeof(Installation).Assembly.GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("程序缺少内置游戏组件。"))
        using (var output = File.Create(zip)) resource.CopyTo(output);

        var extraction = Path.Combine(temp, "payload");
        ZipFile.ExtractToDirectory(zip, extraction);
        var roots = Directory.GetDirectories(extraction);
        if (roots.Length != 1) throw new InvalidDataException("安装包目录结构无效。");
        var root = roots[0];
        foreach (var required in new[] { "WukongGuard.dll", "rules.json", "experience.json",
                     Path.Combine("Overlay", "WukongGuard.Overlay.exe"),
                     Path.Combine("Overlay", "Runtime", "dotnet.exe") })
            if (!File.Exists(Path.Combine(root, required)))
                throw new InvalidDataException("安装包缺少文件：" + required);
        VerifyManifest(root);
        return root;
    }

    private static void VerifyManifest(string root)
    {
        var manifest = Path.Combine(root, "SHA256SUMS.txt");
        if (!File.Exists(manifest)) throw new InvalidDataException("安装包缺少 SHA256 文件清单。");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var count = 0;
        foreach (var line in File.ReadLines(manifest))
        {
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator != 64) throw new InvalidDataException("安装包哈希清单格式错误。");
            var relative = line[(separator + 2)..].Replace('/', Path.DirectorySeparatorChar);
            var file = Path.GetFullPath(Path.Combine(root, relative));
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                throw new InvalidDataException("安装包文件路径无效：" + relative);
            using var input = File.OpenRead(file);
            var actual = Convert.ToHexString(SHA256.HashData(input));
            if (!actual.Equals(line[..64], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包文件校验失败：" + relative);
            count++;
        }
        if (count < 20) throw new InvalidDataException("安装包文件清单不完整。");
    }

    private static async Task EnsureLoaderAsync(string gameBin, string temp, Action<string> progress)
    {
        var loader = Path.Combine(gameBin, "CSharpLoader", "CSharpModBase.dll");
        if (File.Exists(loader))
        {
            progress("检测到已安装的 B1CSharpLoader。");
            return;
        }
        if (File.Exists(Path.Combine(gameBin, "version.dll"))
            || Directory.Exists(Path.Combine(gameBin, "CSharpLoader")))
            throw new InvalidOperationException("检测到其他 version.dll 或部分加载器文件。为避免覆盖其他 Mod，请先手动处理加载器冲突。");

        progress("从作者官方 Release 下载 B1CSharpLoader…");
        var zip = Path.Combine(temp, "loader.zip");
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("RegretPill/0.5.0");
            using var response = await client.GetAsync(LoaderUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxLoaderBytes)
                throw new InvalidDataException("B1CSharpLoader 下载文件大小异常。");
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(zip);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) != 0)
            {
                total += read;
                if (total > MaxLoaderBytes) throw new InvalidDataException("B1CSharpLoader 下载文件大小异常。");
                await output.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        using (var input = File.OpenRead(zip))
        {
            var hash = Convert.ToHexString(SHA256.HashData(input));
            if (!hash.Equals(LoaderSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("B1CSharpLoader 官方文件 SHA256 校验失败。安装已停止。");
        }
        progress("校验加载器并安装…");
        var extraction = Path.Combine(temp, "loader");
        ZipFile.ExtractToDirectory(zip, extraction);
        var sourceBin = Path.Combine(extraction, "b1", "Binaries", "Win64");
        var sourceLoader = Path.Combine(sourceBin, "CSharpLoader");
        if (!File.Exists(Path.Combine(sourceBin, "version.dll"))
            || !File.Exists(Path.Combine(sourceLoader, "CSharpModBase.dll")))
            throw new InvalidDataException("B1CSharpLoader 官方文件结构无效。");
        CopyDirectory(sourceLoader, Path.Combine(gameBin, "CSharpLoader"));
        File.Copy(Path.Combine(sourceBin, "version.dll"), Path.Combine(gameBin, "version.dll"));
        File.WriteAllText(Path.Combine(gameBin, "CSharpLoader", "b1cs.ini"),
            "[Settings]\r\nDevelop=0\r\nConsole=0\r\nEnableJit=0\r\n", new UTF8Encoding(false));
        Log("Installed B1CSharpLoader v0.0.8 from official Release; EnableJit=0.");
    }

    private static void ConfigureLoader(string gameBin)
    {
        var ini = Path.Combine(gameBin, "CSharpLoader", "b1cs.ini");
        if (!File.Exists(ini))
            throw new InvalidOperationException("B1CSharpLoader 配置文件缺失，准备已停止。");
        var original = File.ReadAllText(ini);
        var updated = SetSetting(SetSetting(original, "Console", "0"), "EnableJit", "0");
        if (updated == original) return;
        var backup = ini + ".before-regretpill-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
            + "-" + Guid.NewGuid().ToString("N")[..6] + ".bak";
        File.Copy(ini, backup, false);
        File.WriteAllText(ini, updated, new UTF8Encoding(false));
        Log("B1CSharpLoader Console=0 and EnableJit=0; previous settings backed up: " + backup);
    }

    private static string SetSetting(string original, string key, string value)
    {
        var lineEnding = original.Contains("\r\n") ? "\r\n" : "\n";
        var lines = Regex.Split(original.TrimEnd('\r', '\n'), @"\r?\n");
        var pattern = new Regex(@"^[ \t]*" + Regex.Escape(key) + @"[ \t]*=",
            RegexOptions.IgnoreCase);
        var updated = new List<string>(lines.Length + 1);
        var found = false;
        foreach (var line in lines)
        {
            if (!pattern.IsMatch(line)) { updated.Add(line); continue; }
            if (found) continue;
            updated.Add(key + "=" + value);
            found = true;
        }
        if (!found) updated.Add(key + "=" + value);
        return string.Join(lineEnding, updated) + lineEnding;
    }

    private static void InstallGuard(string packageRoot, string gameBin)
    {
        var loaderDir = Path.Combine(gameBin, "CSharpLoader");
        var modDir = Path.Combine(loaderDir, "Mods", "WukongGuard");
        if (Directory.Exists(modDir))
        {
            var backup = Path.Combine(loaderDir, "Backups",
                "WukongGuard-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            CopyDirectory(modDir, backup);
            Log("Previous installation backed up: " + backup);
        }
        Directory.CreateDirectory(modDir);
        File.Copy(Path.Combine(packageRoot, "WukongGuard.dll"),
            Path.Combine(modDir, "WukongGuard.mod-disabled"), true);
        File.Delete(Path.Combine(modDir, "WukongGuard.dll"));
        File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WukongGuard", "active-session.txt"));
        File.Copy(Path.Combine(packageRoot, "rules.json"), Path.Combine(modDir, "rules.json"), true);
        var experience = Path.Combine(modDir, "experience.json");
        if (!File.Exists(experience)) File.Copy(Path.Combine(packageRoot, "experience.json"), experience);
        var owned = new List<string>
        {
            "WukongGuard.dll", "WukongGuard.mod-disabled",
            ".product-version.txt", ".package-sha256.txt"
        };
        CopyDirectory(Path.Combine(packageRoot, "Overlay"), Path.Combine(modDir, "Overlay"), owned, "Overlay");
        CopyDirectory(Path.Combine(packageRoot, "data"), Path.Combine(modDir, "data"), owned, "data");
        foreach (var name in new[] { "find-game.ps1", "start-session.ps1", "uninstall.ps1", "export-diagnostics.ps1", "README.md" })
        {
            var source = Path.Combine(packageRoot, "release", name);
            var destination = Path.Combine(modDir, "release", name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
            owned.Add(Path.Combine("release", name));
        }
        File.WriteAllLines(Path.Combine(modDir, ".release-files.txt"), owned, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(modDir, ".product-version.txt"), ProductVersion,
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(modDir, ".package-sha256.txt"), EmbeddedPayloadHash.Value,
            new UTF8Encoding(false));
    }

    private static void GrantSessionAccess(string modDir, string? requestedSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User?.Value
            ?? throw new InvalidOperationException("无法识别当前 Windows 用户。");
        var sid = requestedSid ?? currentSid;
        if (!sid.StartsWith("S-1-", StringComparison.Ordinal)
            || !sid.Skip(4).All(character => char.IsDigit(character) || character == '-'))
            throw new InvalidOperationException("Windows 用户标识格式无效。");
        var start = new ProcessStartInfo("icacls.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(modDir);
        start.ArgumentList.Add("/grant");
        start.ArgumentList.Add("*" + sid + ":(OI)(CI)M");
        start.ArgumentList.Add("/T");
        start.ArgumentList.Add("/Q");
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("无法设置后悔药组件目录权限。");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("后悔药组件目录权限配置失败：" + error);
        Log("Granted session access on own Mod directory to " + sid);
    }

    internal static void RepairSessionAccess(string gameRoot, string userSid)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        if (!IsGameRoot(gameRoot) || !SessionControl.IsInstalled(gameRoot))
            throw new InvalidOperationException("游戏组件尚未准备完成。");
        GrantSessionAccess(SessionControl.ModDirectory(gameRoot), userSid);
    }

    private static void CopyDirectory(string source, string destination,
        List<string>? owned = null, string? relativeRoot = null)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var output = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output, true);
            if (owned != null) owned.Add(Path.Combine(relativeRoot!, relative));
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WukongGuard-Installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(target).StartsWith("WukongGuard-Installer-", StringComparison.Ordinal)
            && Directory.Exists(target)) Directory.Delete(target, true);
    }

    internal static void Log(string message)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WukongGuard");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "installer.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
