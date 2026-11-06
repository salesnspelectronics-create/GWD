namespace GenericWeb.DeploymentManager.Services;

// Deployment paths are literal filesystem identities, never shell patterns.
public static class DeploymentPathSafety
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, System.Text.StringBuilder targetPath, int maximumLength);

    public static string RequireExclusiveLocalTarget(string path)
    {
        var full = RequireApplicationDirectory(path);
        var root = Path.GetPathRoot(Path.GetFullPath(full))!;
        // UNC/share aliases and mapped/SUBST drives cannot be proven to share
        // the same stable volume-root lease. Keep profiles intact, but fail closed.
        if (!OperatingSystem.IsWindows() || root.Length != 3 || root[1] != ':'
            || new DriveInfo(root).DriveType != DriveType.Fixed)
            throw new IOException("Exclusive deployment requires a local fixed-volume path; UNC and mapped targets require a verified coordination mechanism.");
        var device = new System.Text.StringBuilder(4096);
        if (QueryDosDevice(root[..2], device, device.Capacity) == 0)
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        if (!device.ToString().StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Deployment drive alias cannot establish stable volume-root ownership: " + root);
        return full;
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
            throw new IOException("An absolute, non-device deployment path is required: " + path);
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        foreach (var segment in full[root.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.Contains(':') || segment.Contains('~'))
                throw new IOException("Ambiguous deployment path segment: " + segment);
        var current = full;
        while (!string.IsNullOrEmpty(current))
        {
            // GetAttributes also detects links whose target no longer exists.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Deployment path traverses a reparse point: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
        return full.TrimEnd('\\', '/');
    }

    public static bool Contains(string parent, string child)
        => string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)
           || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void RequireSeparate(string first, string second)
    {
        var a = Normalize(first);
        var b = Normalize(second);
        if (Contains(a, b) || Contains(b, a))
            throw new IOException($"Deployment trees overlap: {a}; {b}");
    }

    public static string RequireApplicationDirectory(string path)
    {
        var full = Normalize(path);
        var root = Path.GetPathRoot(Path.GetFullPath(path))!.TrimEnd('\\', '/');
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Drive and share roots cannot be deployment targets.");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows) && Contains(Normalize(windows), full))
            throw new IOException("Windows directories cannot be deployment targets.");
        // Protect actual system container identities and their ancestors, allowing app subfolders.
        foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                     Environment.SpecialFolder.UserProfile })
        {
            var value = Environment.GetFolderPath(special);
            if (!string.IsNullOrWhiteSpace(value) && Contains(full, Normalize(value)))
                throw new IOException("Deployment target contains a protected system container: " + full);
        }
        return full;
    }

    public static void RequirePlainTree(string path)
    {
        var full = Normalize(path);
        if (!Directory.Exists(full)) return;
        void Visit(string directory)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Deployment tree contains a reparse point: " + item);
                if ((attributes & FileAttributes.Directory) != 0) Visit(item);
            }
        }
        Visit(full);
    }

    public static string RelativeFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOfAny(new[] { '*', '?', ':' }) >= 0)
            throw new IOException("Preserved production file must be a literal relative path: " + relative);
        var parent = Normalize(root);
        var full = Normalize(Path.Combine(parent, relative));
        if (!Contains(parent, full) || string.Equals(parent, full, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Preserved production file escapes its deployment root: " + relative);
        return full;
    }
}
