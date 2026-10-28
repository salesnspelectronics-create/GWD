using GenericWeb.DeploymentManager.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace GenericWeb.DeploymentManager.Services;

public sealed class SqlServerService
{
    private const string RecoveryApplicationName = "GenericWeb.DeploymentManager";
    private readonly CommandRunner _runner;
    private readonly PrerequisiteService _prereq;
    public SqlConnectionStrategy Connections { get; }

    public SqlServerService(CommandRunner runner, PrerequisiteService prereq, SqlConnectionStrategy? connections = null)
    {
        _runner = runner;
        _prereq = prereq;
        Connections = connections ?? new SqlConnectionStrategy(_runner.Report, ConnectionCandidates);
    }

    public List<string> DetectInstanceNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                    result.Add(name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "." : @".\" + name);
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Detect SQL instance names"); }
        }
        return result.ToList();
    }

    public Task<List<SqlInstanceInfo>> DetectInstancesAsync()
        => Task.FromResult(DetectInstanceNames().Select(ReadInstanceMetadata).ToList());

    private static SqlInstanceInfo ReadInstanceMetadata(string name)
    {
        var id = GetInstanceId(name);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var setup = root.OpenSubKey($@"SOFTWARE\Microsoft\Microsoft SQL Server\{id}\Setup");
                if (setup is not null)
                    return new(name, setup.GetValue("Edition")?.ToString() ?? "Detected (registry)",
                        setup.GetValue("PatchLevel")?.ToString() ?? setup.GetValue("Version")?.ToString() ?? "");
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "SQL registry discovery"); }
        }
        return new(name, "Detected (registry; no authentication probe)", "");
    }

    public async Task<SqlInstanceDiagnostics> DiagnoseAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var fingerprint = Connections.Fingerprint(profile);
        var instance = profile.SqlInstance;
        if (string.IsNullOrWhiteSpace(instance))
            return new SqlInstanceDiagnostics(instance, "Unknown", false, false, false, false, "Unknown", "No SQL instance selected.");

        var serviceName = ServiceNameFor(instance);
        var serviceState = await GetServiceStateAsync(serviceName, ct);
        var tcpEnabled = IsTcpEnabled(instance);
        var mixedMode = GetLoginMode(instance) == 2;
        var browserState = InstanceNameFromServer(instance).Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
            ? "Not required for default instance"
            : await GetServiceStateAsync("SQLBrowser", ct);

        try
        {
            await using var connection = await Connections.OpenAsync(profile, "master", ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT
    IS_SRVROLEMEMBER('sysadmin'),
    CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int),
    ISNULL((SELECT CAST(is_disabled AS int) FROM sys.server_principals WHERE name=N'sa'), 1);";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var sysadmin = !reader.IsDBNull(0) && reader.GetInt32(0) == 1;
            var windowsOnly = !reader.IsDBNull(1) && reader.GetInt32(1) == 1;
            var saEnabled = !reader.IsDBNull(2) && reader.GetInt32(2) == 0;
            return new SqlInstanceDiagnostics(instance, serviceState, tcpEnabled, true, sysadmin, saEnabled, windowsOnly ? "Windows only" : "Mixed mode", $"SQL Browser: {browserState}. Connection: {Connections.State(profile).Detail}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Connections.Invalidate(profile, "SQL diagnostic query failed: " + ex.Message, fingerprint);
            return new SqlInstanceDiagnostics(instance, serviceState, tcpEnabled, false, false, false, mixedMode ? "Mixed mode (registry)" : "Windows only/unknown", $"SQL Browser: {browserState}. {Connections.State(profile).Detail}");
        }
    }

    public async Task InstallExpressFromFileAsync(string installerPath, string instanceName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException("SQL Server installer was not found. Choose or download it on the Installers page first.", installerPath);

        if (Path.GetFileName(installerPath).Equals("setup.exe", StringComparison.OrdinalIgnoreCase))
        {
            var account = Environment.UserDomainName + "\\" + Environment.UserName;
            var args = $"/Q /ACTION=Install /FEATURES=SQLEngine /INSTANCENAME={instanceName} /SQLSYSADMINACCOUNTS=\"{account}\" /IACCEPTSQLSERVERLICENSETERMS /SUPPRESSPRIVACYSTATEMENTNOTICE /TCPENABLED=1";
            await _runner.RunCheckedAsync(installerPath, args, Path.GetDirectoryName(installerPath), ct, TimeSpan.FromMinutes(30));
        }
        else
        {
            await _runner.RunInteractiveAsync(installerPath, ct: ct);
        }
    }

    public async Task RepairLocalConnectivityAsync(string instance, CancellationToken ct = default, DeploymentProfile? profile = null)
    {
        EnsureLocalInstance(instance);
        _runner.Report($"[SQL] Repairing local SQL connectivity for {instance}...");

        var changed = EnableTcpRegistry(instance);
        if (changed)
            _runner.Report("[SQL] TCP/IP protocol was disabled. It has been enabled in the SQL Server instance registry configuration.");
        else
            _runner.Report("[SQL] TCP/IP protocol is already enabled or no registry change was required.");

        var instanceName = InstanceNameFromServer(instance);
        if (!instanceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureSqlBrowserAsync(ct);
        }

        var serviceName = ServiceNameFor(instance);
        var state = await GetServiceStateAsync(serviceName, ct);
        if (state.Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            if (changed)
            {
                _runner.Report($"[SQL] Restarting {serviceName} so protocol changes take effect...");
                await StopServiceCheckedAsync(serviceName, ct);
                await StartServiceCheckedAsync(serviceName, null, ct);
            }
        }
        else
        {
            _runner.Report($"[SQL] SQL service {serviceName} is {state}; starting it...");
            await StartServiceCheckedAsync(serviceName, null, ct);
        }

        await WaitForServiceStateAsync(serviceName, "RUNNING", TimeSpan.FromSeconds(30), ct);
        await Task.Delay(1500, ct);

        await using var test = profile is null
            ? await OpenConnectionAsync(instance, "master", true, null, null, ct)
            : await Connections.OpenAsync(profile, "master", ct);
        _runner.Report("[SQL] Local SQL connectivity repair succeeded using the requested authentication.");
    }

    public async Task TestWindowsAdminAsync(string instance, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(instance, "master", true, null, null, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IS_SRVROLEMEMBER('sysadmin')";
        var value = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (value != 1)
            throw new InvalidOperationException("Windows Authentication reached SQL Server, but the current Windows account is not a SQL Server sysadmin. Use 'Emergency recover SQL access' on this page if this is a local instance and you are a Windows administrator.");
    }

    public async Task TestConnectionAsync(DeploymentProfile profile)
        => await TestConnectionWithSafeFallbackAsync(profile);

    public async Task<string> TestConnectionWithSafeFallbackAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        await using var connection = await Connections.OpenAsync(profile, "master", ct);
        return Connections.State(profile).Detail;
    }

    public async Task TestSqlLoginAsync(string instance, string userName, string password, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(instance, "master", false, userName, password, ct);
    }

    public async Task ConfigureOrResetSaAsync(string instance, string password, CancellationToken ct = default)
    {
        ValidateSaRequest(instance, password);

        try
        {
            await TestWindowsAdminAsync(instance, ct);
        }
        catch (SqlException)
        {
            _runner.Report("[SQL] Initial Windows connection failed. Attempting local SQL service/protocol repair before SA reset...");
            await RepairLocalConnectivityAsync(instance, ct);
            await TestWindowsAdminAsync(instance, ct);
        }

        var integrated = await OpenConnectionAsync(instance, "master", true, null, null, ct);
        await using (integrated)
        {
            await using var mode = integrated.CreateCommand();
            mode.CommandText = "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)";
            var windowsOnly = Convert.ToInt32(await mode.ExecuteScalarAsync(ct)) == 1;

            await using var command = integrated.CreateCommand();
            command.CommandText = $@"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name=N'sa')
    THROW 50001, 'The built-in sa login is not named sa on this instance.', 1;
ALTER LOGIN [sa] ENABLE;
ALTER LOGIN [sa] WITH PASSWORD = N'{Lit(password)}' UNLOCK;";
            await command.ExecuteNonQueryAsync(ct);

            if (windowsOnly)
                SetMixedModeRegistry(instance);
        }

        if (GetLoginMode(instance) == 2)
        {
            var serviceName = ServiceNameFor(instance);
            _runner.Report("[SQL] Restarting SQL Server to apply Mixed Mode authentication...");
            await StopServiceCheckedAsync(serviceName, ct);
            await StartServiceCheckedAsync(serviceName, null, ct);
            await WaitForServiceStateAsync(serviceName, "RUNNING", TimeSpan.FromSeconds(30), ct);
            await Task.Delay(1500, ct);
        }

        await TestSqlLoginAsync(instance, "sa", password, ct);
        _runner.Report("[SQL] SA configuration/reset completed and the new SQL login was verified.");
    }

    public async Task EmergencyRecoverSqlAccessAsync(string instance, string password, CancellationToken ct = default)
    {
        ValidateSaRequest(instance, password);
        EnsureLocalInstance(instance);

        var serviceName = ServiceNameFor(instance);
        var agentName = AgentServiceNameFor(instance);
        var agentWasRunning = (await GetServiceStateAsync(agentName, ct)).Equals("RUNNING", StringComparison.OrdinalIgnoreCase);
        var normalRestartNeeded = false;

        _runner.Report("[SQL RECOVERY] Starting emergency local SQL access recovery.");
        _runner.Report("[SQL RECOVERY] The SQL Server service will be restarted temporarily in single-user mode. Other SQL applications should remain closed until recovery completes.");

        try
        {
            EnableTcpRegistry(instance);
            SetMixedModeRegistry(instance);
            if (!InstanceNameFromServer(instance).Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                await EnsureSqlBrowserAsync(ct);

            if (agentWasRunning)
            {
                _runner.Report($"[SQL RECOVERY] Stopping SQL Agent service {agentName} so it cannot consume the single-user connection...");
                await StopServiceBestEffortAsync(agentName, ct);
            }

            _runner.Report($"[SQL RECOVERY] Stopping SQL service {serviceName}...");
            await StopServiceBestEffortAsync(serviceName, ct);
            await WaitForServiceNotRunningAsync(serviceName, TimeSpan.FromSeconds(30), ct);

            _runner.Report($"[SQL RECOVERY] Starting {serviceName} in restricted single-user mode for this Deployment Manager only...");
            await StartServiceCheckedAsync(serviceName, $"/m\"{RecoveryApplicationName}\"", ct);
            normalRestartNeeded = true;
            await WaitForServiceStateAsync(serviceName, "RUNNING", TimeSpan.FromSeconds(30), ct);

            SqlConnection? recoveryConnection = null;
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 15 && recoveryConnection is null; attempt++)
            {
                try
                {
                    recoveryConnection = await OpenConnectionAsync(instance, "master", true, null, null, ct, RecoveryApplicationName, pooling: false, connectTimeout: 3);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _runner.Report($"[SQL RECOVERY] Waiting for the single-user SQL connection (attempt {attempt}/15): {ShortMessage(ex)}");
                    await Task.Delay(1500, ct);
                }
            }

            if (recoveryConnection is null)
                throw new InvalidOperationException("SQL Server entered single-user recovery mode, but the Deployment Manager could not obtain the recovery connection. Close SSMS/Visual Studio/other SQL tools and try again.", lastError);

            await using (recoveryConnection)
            {
                await using var command = recoveryConnection.CreateCommand();
                command.CommandText = $@"
DECLARE @login sysname = SUSER_SNAME();
DECLARE @quoted nvarchar(520) = QUOTENAME(@login);
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name=@login)
    EXEC(N'CREATE LOGIN ' + @quoted + N' FROM WINDOWS');
EXEC(N'ALTER SERVER ROLE [sysadmin] ADD MEMBER ' + @quoted);
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name=N'sa')
BEGIN
    ALTER LOGIN [sa] ENABLE;
    ALTER LOGIN [sa] WITH PASSWORD = N'{Lit(password)}' UNLOCK;
END;";
                await command.ExecuteNonQueryAsync(ct);
            }

            _runner.Report("[SQL RECOVERY] Current Windows account was granted SQL sysadmin and SA was enabled/reset.");
        }
        finally
        {
            if (normalRestartNeeded)
            {
                _runner.Report("[SQL RECOVERY] Returning SQL Server to normal multi-user mode...");
                await StopServiceBestEffortAsync(serviceName, CancellationToken.None);
                await StartServiceCheckedAsync(serviceName, null, CancellationToken.None);
                await WaitForServiceStateAsync(serviceName, "RUNNING", TimeSpan.FromSeconds(30), CancellationToken.None);
            }

            if (agentWasRunning)
            {
                _runner.Report($"[SQL RECOVERY] Restoring SQL Agent service {agentName}...");
                await StartServiceBestEffortAsync(agentName, CancellationToken.None);
            }
        }

        await Task.Delay(1500, ct);
        await TestWindowsAdminAsync(instance, ct);
        await TestSqlLoginAsync(instance, "sa", password, ct);
        _runner.Report("[SQL RECOVERY] Recovery verified: Windows sysadmin access and SA login both work.");
    }

    public async Task CreateDatabaseAndAppPoolLoginAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var fingerprint = Connections.Fingerprint(profile);
        try { await PrepareDatabaseAsync(profile, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Connections.Invalidate(profile, ex.Message, fingerprint); throw; }
    }

    private async Task PrepareDatabaseAsync(DeploymentProfile profile, CancellationToken ct)
    {
        var dbName = profile.DatabaseName.Trim();
        if (string.IsNullOrWhiteSpace(dbName)) throw new InvalidOperationException("Database name is required.");

        _runner.Report($"[DATABASE] Ensuring database '{dbName}' exists...");
        await using (var master = await Connections.OpenAsync(profile, "master", ct))
        {
            await using var create = master.CreateCommand();
            create.CommandText = $"IF DB_ID(N'{Lit(dbName)}') IS NULL CREATE DATABASE {Id(dbName)};";
            await create.ExecuteNonQueryAsync(ct);
        }

        // CREATE DATABASE and USE/CREATE USER must not share one batch. Reconnect to the
        // newly-created database only after SQL Server reports it ONLINE.
        var online = false;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            await using var master = await Connections.OpenAsync(profile, "master", ct);
            await using var state = master.CreateCommand();
            state.CommandText = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.databases WHERE name=N'{Lit(dbName)}' AND state_desc=N'ONLINE') THEN 1 ELSE 0 END;";
            online = Convert.ToInt32(await state.ExecuteScalarAsync(ct)) == 1;
            if (online) break;
            await Task.Delay(500, ct);
        }
        if (!online) throw new InvalidOperationException($"Database '{dbName}' was created but did not become ONLINE in time.");
        _runner.Report($"[DATABASE] Database '{dbName}' exists and is ONLINE.");

        if (!Connections.State(profile).Integrated)
        {
            await using var sqlDatabase = await Connections.OpenAsync(profile, dbName, ct);
            _runner.Report("[DATABASE] Database access verified using SQL Authentication; configured SQL principal retained.");
            return;
        }

        var poolPrincipal = $"IIS APPPOOL\\{profile.AppPoolName}";
        await using (var master = await Connections.OpenAsync(profile, "master", ct))
        {
            await using var login = master.CreateCommand();
            login.CommandText = $"IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name=N'{Lit(poolPrincipal)}') CREATE LOGIN {Id(poolPrincipal)} FROM WINDOWS;";
            await login.ExecuteNonQueryAsync(ct);
        }

        await using (var database = await Connections.OpenAsync(profile, dbName, ct))
        {
            await using var user = database.CreateCommand();
            user.CommandText = $@"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'{Lit(poolPrincipal)}')
    CREATE USER {Id(poolPrincipal)} FOR LOGIN {Id(poolPrincipal)};
IF IS_ROLEMEMBER(N'db_owner', N'{Lit(poolPrincipal)}') <> 1
    ALTER ROLE db_owner ADD MEMBER {Id(poolPrincipal)};";
            await user.ExecuteNonQueryAsync(ct);
        }
        _runner.Report($"[DATABASE] IIS application-pool principal '{poolPrincipal}' is ready in '{dbName}'.");
    }

    public string BuildConnectionString(DeploymentProfile profile, string? database = null)
        => Connections.BuildConnectionString(profile, database);

    private Task<SqlConnection> OpenConnectionAsync(string instance, string database, bool integrated,
        string? user, string? password, CancellationToken ct, string applicationName = RecoveryApplicationName,
        bool pooling = true, int connectTimeout = 8)
        => Connections.OpenExplicitAsync(instance, database, integrated, user, password, ct, applicationName, pooling, connectTimeout);

    private IEnumerable<string> ConnectionCandidates(string instance)
    {
        yield return instance;
        if (!IsLocalInstance(instance)) yield break;

        var port = ReadConfiguredTcpPort(instance);
        if (!string.IsNullOrWhiteSpace(port) && port != "0")
            yield return $"tcp:127.0.0.1,{port}";

        var instanceName = InstanceNameFromServer(instance);
        if (instanceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
            yield return "tcp:127.0.0.1,1433";
    }

    private async Task EnsureSqlBrowserAsync(CancellationToken ct)
    {
        var state = await GetServiceStateAsync("SQLBrowser", ct);
        if (state.Equals("NOT_INSTALLED", StringComparison.OrdinalIgnoreCase))
        {
            _runner.Report("[SQL] SQL Server Browser service is not installed; named-instance resolution will rely on the configured TCP port.");
            return;
        }

        if (!state.Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            _runner.Report("[SQL] Enabling/starting SQL Server Browser for named-instance discovery...");
            await _runner.RunAsync("sc.exe", "config SQLBrowser start= demand", ct: ct, timeout: TimeSpan.FromSeconds(20));
            await StartServiceBestEffortAsync("SQLBrowser", ct);
        }
    }

    private async Task<string> GetServiceStateAsync(string serviceName, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("sc.exe", $"query \"{serviceName}\"", ct: ct, timeout: TimeSpan.FromSeconds(10));
        var text = result.Output + "\n" + result.Error;
        if (result.ExitCode != 0 && text.Contains("1060", StringComparison.OrdinalIgnoreCase)) return "NOT_INSTALLED";
        if (text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return "RUNNING";
        if (text.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return "STOPPED";
        if (text.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase)) return "START_PENDING";
        if (text.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase)) return "STOP_PENDING";
        return result.ExitCode == 0 ? "UNKNOWN" : "UNAVAILABLE";
    }

    private async Task StartServiceCheckedAsync(string serviceName, string? serviceArgument, CancellationToken ct)
    {
        var args = $"start \"{serviceName}\"" + (string.IsNullOrWhiteSpace(serviceArgument) ? string.Empty : " " + serviceArgument);
        var result = await _runner.RunAsync("net.exe", args, ct: ct, timeout: TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            var state = await GetServiceStateAsync(serviceName, ct);
            if (!state.Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Could not start SQL service {serviceName}.\r\n{result.Error}\r\n{result.Output}");
        }
    }

    private async Task StopServiceCheckedAsync(string serviceName, CancellationToken ct)
    {
        var result = await _runner.RunAsync("net.exe", $"stop \"{serviceName}\" /y", ct: ct, timeout: TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            var state = await GetServiceStateAsync(serviceName, ct);
            if (!state.Equals("STOPPED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Could not stop SQL service {serviceName}.\r\n{result.Error}\r\n{result.Output}");
        }
    }

    private async Task StopServiceBestEffortAsync(string serviceName, CancellationToken ct)
    {
        var state = await GetServiceStateAsync(serviceName, ct);
        if (state.Equals("NOT_INSTALLED", StringComparison.OrdinalIgnoreCase) || state.Equals("STOPPED", StringComparison.OrdinalIgnoreCase)) return;
        await _runner.RunAsync("net.exe", $"stop \"{serviceName}\" /y", ct: ct, timeout: TimeSpan.FromMinutes(2));
    }

    private async Task StartServiceBestEffortAsync(string serviceName, CancellationToken ct)
    {
        var state = await GetServiceStateAsync(serviceName, ct);
        if (state.Equals("NOT_INSTALLED", StringComparison.OrdinalIgnoreCase) || state.Equals("RUNNING", StringComparison.OrdinalIgnoreCase)) return;
        await _runner.RunAsync("net.exe", $"start \"{serviceName}\"", ct: ct, timeout: TimeSpan.FromMinutes(2));
    }

    private async Task WaitForServiceStateAsync(string serviceName, string wantedState, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            var state = await GetServiceStateAsync(serviceName, ct);
            if (state.Equals(wantedState, StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException($"Service {serviceName} did not reach state {wantedState} within {timeout.TotalSeconds:0} seconds.");
    }

    private async Task WaitForServiceNotRunningAsync(string serviceName, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            var state = await GetServiceStateAsync(serviceName, ct);
            if (state.Equals("STOPPED", StringComparison.OrdinalIgnoreCase) || state.Equals("NOT_INSTALLED", StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException($"Service {serviceName} did not stop within {timeout.TotalSeconds:0} seconds.");
    }

    private static bool IsLocalInstance(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName)) return false;
        var host = serverName;
        var slash = host.IndexOf('\\');
        if (slash >= 0) host = host[..slash];
        var comma = host.IndexOf(',');
        if (comma >= 0) host = host[..comma];
        host = host.Trim();
        return host == "."
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureLocalInstance(string instance)
    {
        if (!IsLocalInstance(instance))
            throw new InvalidOperationException("Automatic SQL service repair/recovery is intentionally limited to a SQL Server instance on this machine.");
    }

    private static void ValidateSaRequest(string instance, string password)
    {
        if (string.IsNullOrWhiteSpace(instance)) throw new InvalidOperationException("Select a SQL Server instance first.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new InvalidOperationException("Use a stronger SQL password (at least 10 characters). A generated password is recommended.");
    }

    private static bool IsTcpEnabled(string serverName)
    {
        try
        {
            var instanceId = GetInstanceId(serverName);
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var tcp = root.OpenSubKey($@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceId}\MSSQLServer\SuperSocketNetLib\Tcp");
                if (tcp?.GetValue("Enabled") is int enabled) return enabled == 1;
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read SQL TCP enabled state"); }
        return false;
    }

    private static bool EnableTcpRegistry(string serverName)
    {
        var instanceId = GetInstanceId(serverName) ?? throw new InvalidOperationException("Could not locate this SQL Server instance in the registry.");
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var tcp = root.OpenSubKey($@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceId}\MSSQLServer\SuperSocketNetLib\Tcp", writable: true);
                if (tcp is null) continue;
                var current = Convert.ToInt32(tcp.GetValue("Enabled", 0));
                if (current == 1) return false;
                tcp.SetValue("Enabled", 1, RegistryValueKind.DWord);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Administrator rights are required to enable SQL Server TCP/IP.");
            }
        }
        throw new InvalidOperationException("Could not find the SQL Server TCP/IP registry settings for this instance.");
    }

    private static string? ReadConfiguredTcpPort(string serverName)
    {
        try
        {
            var instanceId = GetInstanceId(serverName);
            if (string.IsNullOrWhiteSpace(instanceId)) return null;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var ipAll = root.OpenSubKey($@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceId}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll");
                if (ipAll is null) continue;
                var fixedPort = ipAll.GetValue("TcpPort")?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(fixedPort) && fixedPort != "0") return fixedPort;
                var dynamicPort = ipAll.GetValue("TcpDynamicPorts")?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(dynamicPort) && dynamicPort != "0") return dynamicPort;
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read configured SQL TCP port"); }
        return null;
    }

    private static void SetMixedModeRegistry(string serverName)
    {
        var instanceId = GetInstanceId(serverName) ?? throw new InvalidOperationException("Could not locate this SQL Server instance in the registry to enable Mixed Mode authentication.");
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var settings = root.OpenSubKey($@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceId}\MSSQLServer", writable: true);
                if (settings is null) continue;
                settings.SetValue("LoginMode", 2, RegistryValueKind.DWord);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Administrator rights are required to enable SQL Server Mixed Mode authentication.");
            }
        }
        throw new InvalidOperationException("Could not locate this SQL Server instance in the registry to enable Mixed Mode authentication.");
    }

    private static int GetLoginMode(string serverName)
    {
        try
        {
            var instanceId = GetInstanceId(serverName);
            if (string.IsNullOrWhiteSpace(instanceId)) return 0;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var settings = root.OpenSubKey($@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceId}\MSSQLServer");
                if (settings?.GetValue("LoginMode") is int mode) return mode;
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read SQL login mode"); }
        return 0;
    }

    private static string? GetInstanceId(string serverName)
    {
        var instanceName = InstanceNameFromServer(serverName);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var names = root.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                var id = names?.GetValue(instanceName)?.ToString();
                if (!string.IsNullOrWhiteSpace(id)) return id;
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Resolve SQL instance registry ID"); }
        }
        return null;
    }

    private static string InstanceNameFromServer(string serverName)
    {
        if (serverName == "." || serverName.Equals("localhost", StringComparison.OrdinalIgnoreCase) || serverName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return "MSSQLSERVER";
        var slash = serverName.LastIndexOf('\\');
        return slash >= 0 ? serverName[(slash + 1)..] : serverName;
    }

    private static string ServiceNameFor(string serverName)
    {
        var instance = InstanceNameFromServer(serverName);
        return instance.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "MSSQLSERVER" : "MSSQL$" + instance;
    }

    private static string AgentServiceNameFor(string serverName)
    {
        var instance = InstanceNameFromServer(serverName);
        return instance.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "SQLSERVERAGENT" : "SQLAgent$" + instance;
    }

    private static string ClassifyConnectionFailure(Exception ex)
    {
        var message = ex.GetBaseException().Message;
        if (ex is DllNotFoundException || message.Contains("SNI.dll", StringComparison.OrdinalIgnoreCase) || message.Contains("0x8007007E", StringComparison.OrdinalIgnoreCase))
            return "SQL client runtime error (SNI/native dependency)";
        if (message.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
            return "login failed";
        if (message.Contains("error: 26", StringComparison.OrdinalIgnoreCase) || message.Contains("Error Locating Server/Instance", StringComparison.OrdinalIgnoreCase))
            return "instance could not be located (SQL service/TCP/SQL Browser configuration)";
        if (message.Contains("network-related", StringComparison.OrdinalIgnoreCase) || message.Contains("server was not found", StringComparison.OrdinalIgnoreCase))
            return "SQL service/network unavailable";
        return "connection not verified: " + ShortMessage(ex);
    }

    private static string ShortMessage(Exception ex)
    {
        var message = ex.GetBaseException().Message.Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length > 180 ? message[..180] + "..." : message;
    }

    private static string Id(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    private static string Lit(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed record SqlInstanceDiagnostics(
    string Instance,
    string ServiceState,
    bool TcpEnabled,
    bool WindowsConnectionWorks,
    bool WindowsUserIsSysadmin,
    bool SaEnabled,
    string AuthenticationMode,
    string Detail)
{
    public string ToDisplayText() =>
        $"Instance: {Instance}\r\n" +
        $"SQL service: {ServiceState}\r\n" +
        $"TCP/IP: {(TcpEnabled ? "Enabled" : "Disabled / unknown")}\r\n" +
        $"Selected-auth connection: {(WindowsConnectionWorks ? "OK" : "Failed")}\r\n" +
        $"Connected account is SQL sysadmin: {(WindowsUserIsSysadmin ? "Yes" : "No / unknown")}\r\n" +
        $"Authentication mode: {AuthenticationMode}\r\n" +
        $"sa enabled: {(SaEnabled ? "Yes" : "No / unknown")}\r\n" +
        $"Detail: {Detail}";
}
