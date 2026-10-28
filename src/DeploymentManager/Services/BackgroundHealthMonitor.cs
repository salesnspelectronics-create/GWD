using GenericWeb.DeploymentManager.Models;
using System.Net.Http;

namespace GenericWeb.DeploymentManager.Services;

public sealed record HealthSnapshot(DateTime CheckedAt, bool IisInstalled, int SiteCount, int AppPoolCount, bool? SqlOk, string SqlDetail, int? HttpStatus, string HttpDetail);

public sealed class BackgroundHealthMonitor : IDisposable
{
    private readonly EnvironmentService _environment;
    private readonly SqlServerService _sql;
    private readonly DeploymentProfile _profile;
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private Task? _loop;
    public HealthSnapshot? Latest { get; private set; }
    public event Action<HealthSnapshot>? Updated;
    public BackgroundHealthMonitor(EnvironmentService environment, SqlServerService sql, DeploymentProfile profile) { _environment=environment; _sql=sql; _profile=profile; }
    public void Start() => _loop ??= Task.Run(LoopAsync);
    public async Task RefreshNowAsync(CancellationToken ct=default) => await CheckAsync(ct);
    public bool HasRecentSuccessfulSql(TimeSpan maxAge) => _sql.Connections.State(_profile).Verified;
    public void ReportSqlSuccess(string detail)
    {
        var previous = Latest;
        var snap = previous is null
            ? new HealthSnapshot(DateTime.Now, false, 0, 0, _sql.Connections.State(_profile).Verified, SecretRedactor.Redact(detail), null, "Not checked")
            : previous with { CheckedAt = DateTime.Now, SqlOk = _sql.Connections.State(_profile).Verified, SqlDetail = SecretRedactor.Redact(detail) };
        Latest = snap;
        Updated?.Invoke(snap);
    }
    public void ReportSqlFailure(string detail)
    {
        var previous = Latest;
        var snap = previous is null
            ? new HealthSnapshot(DateTime.Now, false, 0, 0, false, detail, null, "Not checked")
            : previous with { CheckedAt = DateTime.Now, SqlOk = false, SqlDetail = detail };
        Latest = snap;
        Updated?.Invoke(snap);
    }
    private async Task LoopAsync()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); while (!_cts.IsCancellationRequested) { await CheckAsync(_cts.Token); await Task.Delay(TimeSpan.FromMinutes(10), _cts.Token); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Background health monitor loop"); }
    }
    private async Task CheckAsync(CancellationToken ct)
    {
        bool iis=false; int sites=0,pools=0; bool? sqlOk=null; string sqlDetail="Not configured"; int? httpStatus=null; string httpDetail="Not checked";
        try { iis=(await _environment.CheckIisAsync()).Installed; if(iis){ sites=(await _environment.ListIisSitesAsync(ct)).Count; pools=(await _environment.ListIisAppPoolsAsync(ct)).Count; } } catch(Exception ex){ InternalDiagnostics.Record(ex,"Background IIS health"); }
        if(!string.IsNullOrWhiteSpace(_profile.SqlInstance)) { try { sqlDetail=await _sql.TestConnectionWithSafeFallbackAsync(_profile, ct); sqlOk=_sql.Connections.State(_profile).Verified; } catch(Exception ex){ sqlOk=false; sqlDetail=SecretRedactor.Redact(ex.Message); } }
        if(iis && !string.IsNullOrWhiteSpace(_profile.SiteName)) { try { var uri=new Uri($"http://localhost:{_profile.HttpPort}{(_profile.HealthPath.StartsWith('/')?_profile.HealthPath:"/"+_profile.HealthPath)}"); using var r=await _http.GetAsync(uri,ct); httpStatus=(int)r.StatusCode; httpDetail=$"HTTP {httpStatus}"; } catch(Exception ex){ httpDetail=ex.Message; } }
        var snap=new HealthSnapshot(DateTime.Now,iis,sites,pools,sqlOk,sqlDetail,httpStatus,httpDetail); Latest=snap; Updated?.Invoke(snap);
    }
    public void Dispose(){ _cts.Cancel(); _cts.Dispose(); _http.Dispose(); }
}
