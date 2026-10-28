using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenericWeb.DeploymentManager.Services;

public static class SecretRedactor
{
    private static readonly ConcurrentDictionary<string, byte> Secrets = new(StringComparer.Ordinal);
    public static void Register(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return;
        Secrets.TryAdd(secret, 0);
        Secrets.TryAdd(JsonSerializer.Serialize(secret)[1..^1], 0);
        Secrets.TryAdd(secret.Replace("'", "''"), 0);
        Secrets.TryAdd(secret.Replace("\"", "\"\""), 0);
        Secrets.TryAdd(Uri.EscapeDataString(secret), 0);
    }

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var secret in Secrets.Keys.OrderByDescending(s => s.Length))
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        // JSON strings, quoted connection-string values (including semicolons), SQL literals,
        // and unquoted legacy logs. Redact before persistence and again on diagnostic export.
        text = Regex.Replace(text, "(?i)(\"(?:[a-z_]*password|pwd|token|secret|api[-_]?key)\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"", "$1\"[REDACTED]\"");
        return Regex.Replace(text, "(?i)((?:password|pwd|token|secret|api[-_]?key)\\s*[=:]\\s*)(?:N?'(?:''|[^'])*'|\"(?:\"\"|[^\"])*\"|[^;\\r\\n]*)", "$1[REDACTED]");
    }
}
