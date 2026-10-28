namespace GenericWeb.DeploymentManager.Services;

/// <summary>Fail-closed equality for independently obtained backend and UI release labels.</summary>
internal static class ReleaseVersionContract
{
    internal static bool Matches(string? expected, string? observed)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(observed)) return false;
        expected = expected.Trim();
        observed = observed.Trim();
        if (expected.Length > 128 || observed.Length > 128) return false;
        if (expected.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
            observed.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase);
    }
}
