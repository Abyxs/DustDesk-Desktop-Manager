using System.Windows.Forms;

namespace DustDesk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, "Global\\DustDesk.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DustDesk 已在运行，请不要重复打开。", "DustDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogUnhandledException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                LogUnhandledException(exception);
            }
        };
        Application.Run(new MainForm());
    }

    private static void LogUnhandledException(Exception exception)
    {
        try
        {
            AppLog.WriteException("error.log", exception);
        }
        catch
        {
        }
    }
}

internal static class AppLog
{
    public static void WriteException(string fileName, Exception exception, string? context = null)
    {
        try
        {
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DustDesk", "Logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, fileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{context ?? string.Empty}\r\n{exception}\r\n\r\n");
        }
        catch
        {
        }
    }
}
