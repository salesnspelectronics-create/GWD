namespace GenericWeb.DeploymentManager.Services;

public sealed record CommandResult(int ExitCode, string Output, string Error);
