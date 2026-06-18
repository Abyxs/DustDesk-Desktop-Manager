using System.Windows.Forms;

namespace DustDesk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DustDesk", "Logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{exception}\r\n\r\n");
        }
        catch
        {
        }
    }
}
