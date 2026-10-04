namespace McServerManager.Models.RuntimeLifecycle;

/// <summary>Ownership of this launch only. None of these states certifies a stopped process tree.</summary>
public enum RuntimeLifetimeState
{
    Unknown,
    OwnedRunning,
    OwnedRootExitedUnverified,
    OwnedStopUncertain
}

public sealed record RuntimeLifetimeSnapshot(
    long Generation,
    RuntimeLifetimeState State,
    string RegisteredDirectory,
    bool UsesShellWrapper);
