using System.Diagnostics;

namespace GoZCCondorLauncher;

public enum FlightSessionState
{
    Ready,
    Preparing,
    StartingCondor,
    OpeningFlightPlanner,
    StartingFlight,
    Flying,
    ClosingCondor,
    WaitingForExit,
    Error
}

public enum CondorEndReason { Debriefing, ProcessExited }

public sealed class FlightStateMachine
{
    public FlightSessionState State { get; private set; } = FlightSessionState.Ready;
    public bool TryBegin()
    {
        if (State != FlightSessionState.Ready) return false;
        State = FlightSessionState.Preparing;
        return true;
    }
    public void MoveTo(FlightSessionState state) => State = state;
    public void Reset() => State = FlightSessionState.Ready;
}

internal static class CondorProcessService
{
    public static IReadOnlyList<Process> RunningProcesses()
    {
        try { return Process.GetProcessesByName("Condor").Where(IsAlive).ToArray(); }
        catch (InvalidOperationException) { return []; }
    }

    public static bool AnyRunning() => RunningProcesses().Count > 0;

    public static bool IsAlive(Process process)
    {
        try { return !process.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public static async Task<bool> WaitForAllExitedAsync(TimeSpan timeout, CancellationToken token)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            token.ThrowIfCancellationRequested();
            if (!AnyRunning()) return true;
            await Task.Delay(250, token);
        }
        return !AnyRunning();
    }
}
