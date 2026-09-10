# Compilable STA hosted-service sample

Run on Windows:

```powershell
dotnet run --project samples/StaService/StaService.csproj --configuration Release
```

The executable checks normal results, shutdown with accepted requests, and partial
startup failure. `FakeCalculator` enforces thread affinity during use and disposal.
`CalculationService` implements `IHostedService`; a real host must call Start/Stop
in order and dispose the service only after Stop finishes. Register the scheduler
before the service so the service stops before the scheduler is disposed.

Replace each exclusively owned fake with an `Executor.Create` handle and release
it with `Executor.Free` on the scheduler thread. Release in reverse creation order,
including after partial startup. Do not force-release shared RCWs.

The host's canceled shutdown token does not cancel the queued cleanup operation.
The cleanup **wait** has a five-second budget; cleanup itself remains serialized
behind any active call. If native work never returns, cleanup cannot run and
disposing the scheduler may wait forever. Process isolation is required for hard
termination. This sample does not activate third-party COM components.
