using System.Text;

namespace GenericWeb.DeploymentManager.Services;

public static class InternalDiagnostics
{
    private static readonly object Sync = new();
    private static string? _root;

    public static void Configure(string root) => _root = root;

    public static void Record(Exception ex, string? context = null)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(_root)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NSP", "DeploymentManager")
                : _root!;
            var folder = Path.Combine(root, "InternalDiagnostics");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Internal-{DateTime.Now:yyyyMMdd}.log");
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
            if (!string.IsNullOrWhiteSpace(context)) sb.Append(context).Append(" | ");
            sb.AppendLine(ex.ToString());
            lock (Sync) File.AppendAllText(path, SecretRedactor.Redact(sb.ToString()));
        }
        catch
        {
            // Last-chance diagnostics must never crash the deployment manager.
        }
    }
}
