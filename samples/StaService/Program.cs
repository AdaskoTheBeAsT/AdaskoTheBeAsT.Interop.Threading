using System;
using System.Threading;
using System.Threading.Tasks;
using AdaskoTheBeAsT.Interop.Threading;

namespace StaService;

internal static class Program
{
    public static async Task Main()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        using var service = new CalculationService(scheduler);
        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        var result = await service.AddAsync(20, 22, CancellationToken.None).ConfigureAwait(false);
        if (result != 42)
        {
            throw new InvalidOperationException("Incorrect calculation.");
        }

        await RunShutdownScenarioAsync(service).ConfigureAwait(false);
        await RunStartupFailureScenarioAsync(scheduler).ConfigureAwait(false);
        Console.WriteLine("STA sample passed: result, request shutdown, and partial startup cleanup.");
    }

    private static async Task RunShutdownScenarioAsync(CalculationService service)
    {
        var requests = new Task<decimal>[100];
        for (var i = 0; i < requests.Length; i++)
        {
#pragma warning disable AsyncFixer04 // Every stored request task is awaited after service shutdown.
            requests[i] = service.AddAsync(i, 1, CancellationToken.None);
#pragma warning restore AsyncFixer04
        }

        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await Task.WhenAll(requests).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"Shutdown canceled one or more accepted requests ({ex.HResult}).");
        }

        foreach (var request in requests)
        {
            if (!request.IsCompleted)
            {
                throw new InvalidOperationException("Shutdown stranded a request.");
            }
        }
    }

    private static async Task RunStartupFailureScenarioAsync(SingleThreadedApartmentTaskScheduler scheduler)
    {
        using var failing = new CalculationService(scheduler, failStartup: true);
        try
        {
            await failing.StartAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Expected startup failure.");
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Deliberate partial startup failure.", StringComparison.Ordinal))
        {
            Console.WriteLine($"Startup cleanup succeeded ({ex.HResult}).");
        }
    }
}
