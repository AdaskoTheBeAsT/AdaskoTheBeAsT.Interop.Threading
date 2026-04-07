namespace AdaskoTheBeAsT.Interop.Threading;

internal interface IStaWorkItem
{
    void Execute();

    void Cancel();
}
