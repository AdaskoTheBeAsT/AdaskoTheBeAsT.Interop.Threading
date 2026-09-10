using System;
using System.Threading;
using AdaskoTheBeAsT.Interop.Threading;

namespace StaService;

internal sealed class FakeCalculator : IDisposable
{
    private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
    private bool _disposed;

    public decimal Add(decimal left, decimal right, StaYield yield, CancellationToken token)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        yield.Sleep(5, token);
        return left + right;
    }

    public void Dispose()
    {
        CheckThread();
        _disposed = true;
    }

    private void CheckThread()
    {
        if (_threadId != Thread.CurrentThread.ManagedThreadId || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Component accessed outside its owning STA.");
        }
    }
}
