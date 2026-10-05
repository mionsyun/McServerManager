namespace McServerManager.Models.RuntimeLifecycle;

/// <summary>Root exit is an operational result, never evidence that descendants or directory users exited.</summary>
public enum ProcessStopOutcome
{
    Uncertain,
    RootExitedUnverified
}

public sealed record ProcessStopResult(ProcessStopOutcome Outcome, bool Forced, string? Error = null);
