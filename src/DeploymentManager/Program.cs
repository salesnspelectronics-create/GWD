using System.Diagnostics;
using System.Security.Principal;

namespace GenericWeb.DeploymentManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!IsAdministrator())
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Deployment Manager must run as Administrator.\r\n\r\n{GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(ex.Message)}", "Administrator rights required", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        // Avoid native SNI deployment failures (Microsoft.Data.SqlClient.SNI.dll / 0x8007007E)
        // in the deployment tool itself. The manager primarily connects to local SQL Server instances,
        // so managed networking is a safer deployment-time dependency profile.
        AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => SaveCrash(e.Exception, "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => SaveCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "AppDomain");
        Application.Run(new WizardForm());
    }

    private static void SaveCrash(Exception exception, string source)
    {
        try
        {
            var appData = new Services.AppDataService();
            var path = Path.Combine(appData.CrashReports, $"Crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, Services.SecretRedactor.Redact($"Generic Web Deployment Manager crash\r\nVersion: {Application.ProductVersion}\r\nTime: {DateTimeOffset.Now:O}\r\nSource: {source}\r\nOS: {Environment.OSVersion}\r\nRuntime: {Environment.Version}\r\n\r\n{exception}"));
            MessageBox.Show($"An application error was captured.\r\n\r\n{GenericWeb.DeploymentManager.Services.SecretRedactor.Redact(exception.Message)}\r\n\r\nCrash report:\r\n{path}", "Deployment Manager error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex) { Services.InternalDiagnostics.Record(ex, "Save crash report"); }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) { Services.InternalDiagnostics.Record(ex, "Administrator check"); return false; }
    }
}
