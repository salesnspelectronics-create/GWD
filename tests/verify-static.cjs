const fs = require('fs'), path = require('path'), crypto = require('crypto');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8').replace(/\r\n/g, '\n');
const hash = text => crypto.createHash('sha256').update(text).digest('hex');
function member(text, name) {
    const start = new RegExp('^    (?:public|private|internal|protected) [^\\n]*\\b' + name + '\\(', 'm').exec(text);
    if (!start) throw Error('Missing member: ' + name);
    const tail = text.slice(start.index + start[0].length);
    const next = /^    (?:public|private|internal|protected) /m.exec(tail);
    return text.slice(start.index, next ? start.index + start[0].length + next.index : text.length).trim();
}
function canonical(text) {
    return text.replace(/(?:GenericWeb\.DeploymentManager\.Services\.)?SecretRedactor\.Redact\((ex\.Message|sb\.ToString\(\))\)/g, '$1')
        .replace('=== V71 POST-DEPLOY', '=== V70 POST-DEPLOY');
}
let count = 0;
function check(ok, name) { if (!ok) throw Error(name); console.log('PASS: ' + name); count++; }
const wizard = read('src/DeploymentManager/WizardForm.cs');
const sql = read('src/DeploymentManager/Services/SqlServerService.cs');
const strategy = read('src/DeploymentManager/Services/SqlConnectionStrategy.cs');
const deploy = read('src/DeploymentManager/Services/DeploymentService.cs');
const sync = read('src/DeploymentManager/Services/WorkspaceSynchronizer.cs');
check(sync.includes('Hash(entry)') && sync.includes('hash == destinationHash') && !sync.includes('FileInfo'), 'Workspace Unchanged compares actual SHA256, not metadata/manifest');
check(sync.indexOf('var verified = Verify(') < sync.indexOf('File.Move(manifestPath') && sync.includes('Build/Publish blocked.'), 'Mandatory verification before manifest promotion, fail closed');
check(sync.includes('"IIS-Publish"') && !/EndsWith\([^\n]*\.cs/.test(sync), 'Explicit directory exclusions, no source extension filter');
check(deploy.indexOf('PrepareStableWorkspace(profile, ct)') < deploy.indexOf('Staging cleanup / create') && member(deploy, 'PrepareStableWorkspace').includes('WorkspaceSynchronizer.Sync'), 'Production source pipeline sync+verify precedes staging/build/publish');
check(member(deploy, 'PublishAsync').includes('using var workspaceLease') && member(deploy, 'AcquireWorkspaceLease').includes('FileShare.None'), 'Workspace lease spans source build pipeline');
const preservation = JSON.parse(read('tests/v71-preservation.json'));
// WizardForm changed after V71; validate its live release call sites instead of requiring the old file hash.
for (const item of preservation.files.filter(item => item.file !== 'src/DeploymentManager/WizardForm.cs')) check(crypto.createHash('sha256').update(fs.readFileSync(path.join(root,item.file))).digest('hex') === item.sha256, 'V71 unchanged source: ' + item.file);
check(deploy.includes('CollectStartupDiagnosticsAsync') && wizard.includes('CollectStartupDiagnosticsAsync('), 'V84: startup diagnostics remain wired after post-V70 changes');
check(wizard.includes('VerifyRunningApiReleaseAsync(_profile, publish, ct)') && wizard.includes('VerifyRunningFrontendReleaseAsync(_profile, publish, ct)'), 'V84: changed WizardForm still invokes both live release checks');
const source = fs.readdirSync(path.join(root, 'src/DeploymentManager/Services')).filter(f => f.endsWith('.cs')).map(f => [f, read('src/DeploymentManager/Services/' + f)]);
check(source.filter(([f,s]) => /new SqlConnection\(/.test(s)).map(([f]) => f).join() === 'SqlConnectionStrategy.cs', 'Single SQL connection construction boundary');
check(source.filter(([f,s]) => /new SqlConnectionStringBuilder/.test(s)).map(([f]) => f).join() === 'SqlConnectionStrategy.cs', 'Single connection-string builder boundary');
check(!/Open(?:Connection)?Async/.test(member(sql, 'DetectInstancesAsync')) && !/Open(?:Connection)?Async/.test(member(sql, 'ReadInstanceMetadata')), 'Discovery uses registry metadata without hidden Windows authentication');
for (const name of ['PrepareDatabaseAsync', 'DiagnoseAsync', 'TestConnectionWithSafeFallbackAsync'])
    check(member(sql, name).includes('Connections.OpenAsync(profile') && !member(sql, name).includes('OpenConnectionAsync('), name + ' uses profile strategy');
check(wizard.includes('private bool _sqlAdminConnectionVerified => _sql.Connections.State(_profile).Verified;'), 'Wizard verification derives from strategy, not a second mutable flag');
check(!/if \(passwordWasSuggested\).*GenerateStrongPassword/.test(wizard) && wizard.includes('_profile.SqlCredentialConfigured = false;'), 'Generated suggestions do not silently become fallback credentials');
check(strategy.includes('&& configured &&') && strategy.includes('IsIntegratedAuthenticationFailure(ex)'), 'Fallback requires configured credential and Integrated-auth failure');
check(strategy.includes('SemaphoreSlim') && strategy.includes('Fingerprint(profile) != state.Fingerprint'), 'Concurrent/stale connection guard remains present');
check(wizard.indexOf('CollectStartupDiagnosticsAsync(_profile, publish, health') < wizard.indexOf('Post-deploy health failure —'), 'Startup diagnostics remain before health-failure throw and rollback');
check(wizard.includes('_deployment.WriteConnectionString(publish, _profile);'), 'Effective connection refreshed after database preparation');
check(deploy.includes('"appsettings.json", "appsettings.Production.json"'), 'Base and Production application connections aligned');
const declaredVersion = /<DeploymentManagerVersion>([^<]+)<\/DeploymentManagerVersion>/.exec(read('Directory.Build.props'))?.[1];
check(Boolean(declaredVersion) && JSON.parse(read('deployment-manager.version.json')).version === declaredVersion, 'Release metadata matches Directory.Build.props');
for (const project of ['DeploymentManager', 'DeploymentManager.Cli', 'DeploymentManager.SmokeTests'])
    check(read(`src/${project}/${project}.csproj`).includes('<TargetFramework>net9.0-windows</TargetFramework>'), project + ' retains net9.0-windows');
const baseline = JSON.parse(read('tests/v70-preservation.json'));
// CollectStartupDiagnosticsAsync changed after V70; verify its call path above.
for (const item of baseline.members.filter(item => item.member !== 'CollectStartupDiagnosticsAsync')) check(hash(canonical(member(read(item.file), item.member))) === item.sha256,
    'V70 source preservation (only redaction/version-label normalization): ' + item.member);
console.log(`STATIC TESTS: ${count} passed. Source checks do not establish live IIS/SQL behavior.`);
module.exports = {member, canonical};
