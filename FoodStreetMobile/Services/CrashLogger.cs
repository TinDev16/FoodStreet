using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.Text;

namespace FoodStreetMobile.Services;

public static class CrashLogger
{
    private static int _initialized;

    public static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Write("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            Write("AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
        };
#endif
    }

    public static void Write(string source, Exception? exception)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("-----");
            sb.AppendLine(DateTimeOffset.Now.ToString("O"));
            sb.AppendLine(source);
            if (exception is not null)
            {
                sb.AppendLine(exception.ToString());
            }
            else
            {
                sb.AppendLine("(null exception)");
            }

            File.AppendAllText(LogPath, sb.ToString());
        }
        catch
        {
            // Ignore logging failures.
        }
    }

    public static async Task<string> TryReadAsync()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return string.Empty;
            }

            return await File.ReadAllTextAsync(LogPath);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task TryShareAsync()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "FoodStreet crash log",
                File = new ShareFile(LogPath)
            });
        }
        catch
        {
        }
    }
}

