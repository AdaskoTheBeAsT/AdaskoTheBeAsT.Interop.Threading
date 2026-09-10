namespace AdaskoTheBeAsT.Interop.Threading;

internal readonly struct PumpOutcome(bool quitSeen, int exitCode, bool budgetExhausted)
{
    public bool QuitSeen { get; } = quitSeen;

    public int ExitCode { get; } = exitCode;

    public bool BudgetExhausted { get; } = budgetExhausted;
}
