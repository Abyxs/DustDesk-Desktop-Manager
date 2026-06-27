using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace DustDesk;

internal sealed record UpdateInstallProgress(string Message, int? Percent = null);

internal static class UpdateInstaller
{
    public static async Task<string> PrepareAsync(
        UpdateInfo update,
        string targetDirectory,
        string executablePath,
        int processId,
        IProgress<UpdateInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("当前 Release 没有可自动安装的下载包。");
        }

        progress?.Report(new UpdateInstallProgress("正在准备更新..."));
        targetDirectory = Path.GetFullPath(targetDirectory);
        executablePath = Path.GetFullPath(executablePath);

        var updateRoot = Path.Combine(Path.GetTempPath(), "DustDesk", "Updates", $"{update.VersionText}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        var extractRoot = Path.Combine(updateRoot, "extracted");
        var zipPath = Path.Combine(updateRoot, "DustDesk-update.zip");
        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(extractRoot);

        await DownloadAsync(update.DownloadUrl, zipPath, progress, cancellationToken);
        progress?.Report(new UpdateInstallProgress("正在解压安装包..."));
        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

        progress?.Report(new UpdateInstallProgress("正在校验安装包..."));
        var packageRoot = FindPackageRoot(extractRoot);
        progress?.Report(new UpdateInstallProgress("正在准备替换旧版本..."));
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

    private static async Task DownloadAsync(
        string url,
        string targetPath,
        IProgress<UpdateInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"DustDesk/{UpdateChecker.CurrentVersionText}");

        progress?.Report(new UpdateInstallProgress("正在连接下载服务器..."));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;
        var lastPercent = -1;
        progress?.Report(new UpdateInstallProgress("正在下载更新包...", totalBytes > 0 ? 0 : null));

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            if (totalBytes is > 0)
            {
                var percent = (int)Math.Clamp(downloadedBytes * 100 / totalBytes.Value, 0, 100);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(new UpdateInstallProgress($"正在下载更新包... {percent}%", percent));
                }
            }
            else
            {
                progress?.Report(new UpdateInstallProgress($"正在下载更新包... {FormatBytes(downloadedBytes)}"));
            }
        }

        progress?.Report(new UpdateInstallProgress("下载完成", 100));
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

    private static string FormatBytes(long bytes)
    {
        return bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";
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
