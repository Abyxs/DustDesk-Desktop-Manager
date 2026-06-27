using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace DustDesk;

internal static class UpdateInstaller
{
    public static async Task<string> PrepareAsync(UpdateInfo update, string targetDirectory, string executablePath, int processId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("当前 Release 没有可自动安装的下载包。");
        }

        targetDirectory = Path.GetFullPath(targetDirectory);
        executablePath = Path.GetFullPath(executablePath);

        var updateRoot = Path.Combine(Path.GetTempPath(), "DustDesk", "Updates", $"{update.VersionText}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        var extractRoot = Path.Combine(updateRoot, "extracted");
        var zipPath = Path.Combine(updateRoot, "DustDesk-update.zip");
        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(extractRoot);

        await DownloadAsync(update.DownloadUrl, zipPath, cancellationToken);
        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

        var packageRoot = FindPackageRoot(extractRoot);
        return WriteInstallScript(updateRoot, packageRoot, targetDirectory, executablePath, processId);
    }

    public static void Start(string scriptPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static async Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"DustDesk/{UpdateChecker.CurrentVersionText}");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string FindPackageRoot(string extractRoot)
    {
        if (File.Exists(Path.Combine(extractRoot, "DustDesk.exe")))
        {
            return extractRoot;
        }

        var executable = Directory.EnumerateFiles(extractRoot, "DustDesk.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("下载包中没有找到 DustDesk.exe。");
        }

        return Path.GetDirectoryName(executable) ?? extractRoot;
    }

    private static string WriteInstallScript(string updateRoot, string sourceDirectory, string targetDirectory, string executablePath, int processId)
    {
        var scriptPath = Path.Combine(updateRoot, "install-update.cmd");
        var logPath = Path.Combine(updateRoot, "install.log");
        var script = $"""
@echo off
chcp 65001 >nul
set "SOURCE={sourceDirectory}"
set "TARGET={targetDirectory}"
set "EXE={executablePath}"
set "PID={processId}"
set "LOG={logPath}"
echo Waiting for DustDesk to exit... > "%LOG%"
:wait
tasklist /FI "PID eq %PID%" 2>NUL | findstr /I "%PID%" >NUL
if "%ERRORLEVEL%"=="0" (
    timeout /t 1 /nobreak >nul
    goto wait
)
echo Copying update files... >> "%LOG%"
robocopy "%SOURCE%" "%TARGET%" /E /COPY:DAT /R:30 /W:1 /NP >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
    echo Update failed with code %RC%. >> "%LOG%"
    exit /b %RC%
)
echo Restarting DustDesk... >> "%LOG%"
start "" "%EXE%"
exit /b 0
""";
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return scriptPath;
    }
}
