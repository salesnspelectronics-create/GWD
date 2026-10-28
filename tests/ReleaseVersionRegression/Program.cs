using GenericWeb.DeploymentManager.Services;

var cases = new (string Name, string? Backend, string? Ui, bool Accept)[]
{
    ("same version 573", "573", "573", true),
    ("mixed backend 573 UI 560", "573", "560", false),
    ("reversed mismatch", "560", "573", false),
    ("empty frontend", "573", "", false),
    ("empty backend", "", "573", false),
    ("missing frontend", "573", null, false),
    ("unknown frontend", "573", "unknown", false),
    ("unknown backend", "unknown", "573", false),
    ("whitespace and case", " V573 ", "v573", true),
    ("different suffix", "573", "573-old", false),
    ("unbounded version", new string('x',129), new string('x',129), false),
};
int failures = 0;
foreach (var test in cases)
{
    var actual = ReleaseVersionContract.Matches(test.Backend, test.Ui);
    Console.WriteLine($"{(actual == test.Accept ? "PASS" : "FAIL")}: {test.Name}");
    if (actual != test.Accept) failures++;
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} passed");
return failures == 0 ? 0 : 1;
