using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GenericWeb.DeploymentManager.Models;
using Microsoft.Data.SqlClient;

namespace GenericWeb.DeploymentManager.Services;

public sealed record SqlVerificationState(string Fingerprint, bool Verified, bool DatabaseVerified, bool Integrated, string Detail,
    string Instance, string Database, bool RequestedIntegrated);

// One session state per profile. No expiry, persisted credentials, or implicit Windows probes.
public sealed class SqlConnectionStrategy
{
    private sealed class Session
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public SqlVerificationState? State;
    }
    private readonly ConditionalWeakTable<DeploymentProfile, Session> _sessions = new();
    private readonly byte[] _fingerprintKey = RandomNumberGenerator.GetBytes(32);
    private readonly Func<string, IEnumerable<string>> _endpoints;
    private readonly Func<SqlConnection, CancellationToken, Task> _open;
    private readonly Action<string> _report;

    public SqlConnectionStrategy(Action<string> report, Func<string, IEnumerable<string>> endpoints,
        Func<SqlConnection, CancellationToken, Task>? open = null)
    {
        _report = report;
        _endpoints = endpoints;
        _open = open ?? ((connection, ct) => connection.OpenAsync(ct));
    }

    public string Fingerprint(DeploymentProfile profile) => Convert.ToHexString(HMACSHA256.HashData(_fingerprintKey,
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { profile.SqlInstance.Trim(), profile.DatabaseName.Trim(),
            profile.UseWindowsAuthentication.ToString(), profile.SqlUserName, profile.SqlPassword, profile.SqlCredentialConfigured.ToString() }))));

    public SqlVerificationState State(DeploymentProfile profile)
    {
        var session = _sessions.GetOrCreateValue(profile);
        lock (session)
        {
            var fingerprint = Fingerprint(profile);
            if (session.State?.Fingerprint != fingerprint)
                session.State = new(fingerprint, false, false, profile.UseWindowsAuthentication, "Not verified — SQL configuration changed or new session",
                    profile.SqlInstance, profile.DatabaseName, profile.UseWindowsAuthentication);
            return session.State;
        }
    }

    internal void ShareSession(DeploymentProfile source, DeploymentProfile clone)
    {
        if (Fingerprint(source) != Fingerprint(clone)) throw new InvalidOperationException("Cannot share SQL verification across different configurations.");
        _sessions.Add(clone, _sessions.GetOrCreateValue(source));
    }

    public void Invalidate(DeploymentProfile profile, string reason, string? expectedFingerprint = null)
    {
        var session = _sessions.GetOrCreateValue(profile);
        lock (session)
        {
            if (expectedFingerprint is not null && Fingerprint(profile) != expectedFingerprint) return;
            session.State = State(profile) with { Verified = false, DatabaseVerified = false, Detail = SecretRedactor.Redact(reason) };
        }
    }

    public static string AuthName(bool integrated) => integrated ? "Windows Authentication" : "SQL Authentication";

    public static bool IsIntegratedAuthenticationFailure(Exception error)
    {
        if (error is OperationCanceledException) return false;
        if (error is SqlException sql && (sql.Number == 18452 || sql.Number == 18458)) return true;
        var message = error.Message;
        return message.Contains("untrusted domain", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot be used with Integrated authentication", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SSPI context", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Integrated authentication failed", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildConnectionString(DeploymentProfile profile, string? database = null)
        => Build(profile.SqlInstance, database ?? profile.DatabaseName, State(profile).Integrated, profile.SqlUserName, profile.SqlPassword).ConnectionString;

    internal static SqlConnectionStringBuilder Build(string instance, string database, bool integrated, string? user, string? password,
        string applicationName = "GenericWeb.DeploymentManager", bool pooling = true, int timeout = 8)
    {
        SecretRedactor.Register(password);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instance, InitialCatalog = database, IntegratedSecurity = integrated,
            TrustServerCertificate = true, ConnectTimeout = timeout, MultipleActiveResultSets = true,
            ApplicationName = applicationName, Pooling = pooling, PersistSecurityInfo = false
        };
        builder["Encrypt"] = "False";
        if (!integrated) { builder.UserID = user ?? ""; builder.Password = password ?? ""; }
        return builder;
    }

    public async Task<SqlConnection> OpenAsync(DeploymentProfile profile, string database, CancellationToken ct = default)
    {
        var session = _sessions.GetOrCreateValue(profile);
        await session.Gate.WaitAsync(ct);
        var state = State(profile);
        var integrated = state.Integrated;
        var fallbackAttempted = false;
        try
        {
            var instance = profile.SqlInstance;
            var user = profile.SqlUserName;
            var password = profile.SqlPassword;
            var configured = profile.SqlCredentialConfigured;
            if (!integrated && !profile.SqlCredentialConfigured) throw new InvalidOperationException("SQL Authentication requires an entered or successfully configured credential; a generated suggestion is not eligible.");
            SqlConnection connection;
            try
            {
                connection = await OpenExplicitAsync(instance, database, integrated, user, password, ct);
            }
            catch (Exception ex) when (integrated && IsIntegratedAuthenticationFailure(ex)
                && configured && !string.IsNullOrWhiteSpace(user) && !string.IsNullOrEmpty(password))
            {
                ct.ThrowIfCancellationRequested();
                if (Fingerprint(profile) != state.Fingerprint) throw new OperationCanceledException("SQL configuration changed before fallback.");
                _report("[SQL] Windows Authentication failed (Integrated authentication). Trying configured SQL Authentication fallback.");
                fallbackAttempted = true;
                integrated = false;
                connection = await OpenExplicitAsync(instance, database, false, user, password, ct);
            }
            lock (session)
            {
                // A background result must never verify credentials edited during an await.
                if (ct.IsCancellationRequested || Fingerprint(profile) != state.Fingerprint)
                {
                    connection.Dispose();
                    throw new OperationCanceledException("SQL configuration changed during connection; verify the current configuration.");
                }
                session.State = state with { Verified = true, Integrated = integrated,
                    DatabaseVerified = state.DatabaseVerified || database.Equals(profile.DatabaseName, StringComparison.Ordinal),
                    Detail = AuthName(integrated) + (state.RequestedIntegrated && !integrated ? " fallback verified" : " verified") };
            }
            _report("[SQL] " + State(profile).Detail + "; database: " + database);
            return connection;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var detail = SecretRedactor.Redact("SQL connection failed using " + AuthName(integrated)
                + (fallbackAttempted ? " fallback after Windows Authentication failure" : "") + ": " + ex.Message);
            lock (session)
            {
                if (Fingerprint(profile) == state.Fingerprint) Invalidate(profile, detail);
            }
            // The original provider exception may contain a secret. Sanitized diagnostics retain its type and message.
            throw new InvalidOperationException(detail);
        }
        finally { session.Gate.Release(); }
    }

    // Explicit operator-requested reset/recovery is isolated from deployment verification.
    public async Task<SqlConnection> OpenExplicitAsync(string instance, string database, bool integrated, string? user, string? password,
        CancellationToken ct, string applicationName = "GenericWeb.DeploymentManager", bool pooling = true, int connectTimeout = 8)
    {
        Exception? last = null;
        foreach (var endpoint in _endpoints(instance).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var connection = new SqlConnection(Build(endpoint, database, integrated, user, password, applicationName, pooling, connectTimeout).ConnectionString);
            try { await _open(connection, ct); return connection; }
            catch (OperationCanceledException) { await connection.DisposeAsync(); throw; }
            catch (Exception ex)
            {
                await connection.DisposeAsync();
                _report("[SQL] " + AuthName(integrated) + " failed: " + SecretRedactor.Redact(ex.Message));
                if (IsIntegratedAuthenticationFailure(ex) || ex is SqlException { Number: 18456 or 4060 }) throw;
                last = ex;
            }
        }
        throw last ?? new InvalidOperationException("No SQL endpoint configured.");
    }
}
