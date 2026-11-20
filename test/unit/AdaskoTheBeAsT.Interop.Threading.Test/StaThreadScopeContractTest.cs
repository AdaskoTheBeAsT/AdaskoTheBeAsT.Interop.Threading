using System;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable AsyncFixer04, CA2000, IDISP001, IDISP016, IDISP017, VSTHRD003
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaThreadScopeContractTest
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CreateThreadScope_RunsOnceAndDisposesOnceOnOwnedStaThreadAsync()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var scope = new TrackingScope();
        var createCount = 0;
        var createThreadId = -1;
        var createApt = ApartmentState.Unknown;
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () =>
            {
                Interlocked.Increment(ref createCount);
                createThreadId = Environment.CurrentManagedThreadId;
                createApt = Thread.CurrentThread.GetApartmentState();
                return scope;
            },
        };

        int workThreadId;
        bool disposedDuringWork;
        using (var scheduler = new SingleThreadedApartmentTaskScheduler(options))
        {
            (workThreadId, disposedDuringWork) = await scheduler.RunAsync(
                () => (Environment.CurrentManagedThreadId, scope.DisposeCount > 0),
                CancellationToken.None);
        }

        createCount.Should().Be(1);
        createApt.Should().Be(ApartmentState.STA);
        createThreadId.Should().NotBe(callerThreadId);
        workThreadId.Should().Be(createThreadId);
        disposedDuringWork.Should().BeFalse();
        scope.DisposeCount.Should().Be(1);
        scope.DisposeThreadId.Should().Be(createThreadId);
        scope.DisposeApartment.Should().Be(ApartmentState.STA);
    }

    [Fact]
    public async Task CreateThreadScope_ReturningNull_SucceedsAndRunsWorkAsync()
    {
        var invoked = false;
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () =>
            {
                invoked = true;
                return null;
            },
        };

        using var scheduler = new SingleThreadedApartmentTaskScheduler(options);
        invoked.Should().BeTrue();
        (await scheduler.RunAsync(() => 21, CancellationToken.None)).Should().Be(21);
    }

    [Fact]
    public void CreateThreadScope_Failure_FailsConstructionCleanlyAndUninitializesOle()
    {
        var expected = new InvalidOperationException("manifest activation failed");
        var platform = new ScopeRecordingPlatform();
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () => throw expected,
        };

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget);
        };

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        platform.Worker!.Join(Budget).Should().BeTrue();
        platform.Events.Should().Equal("ole-init", "ole-uninit");
    }

    [Fact]
    public async Task LateScopeCreation_AfterConstructorTimeout_StillDisposesScopeOnceOnStaThreadAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var scope = new TrackingScope();
        var platform = new ScopeRecordingPlatform();
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () =>
            {
                release.Wait(Budget, CancellationToken.None);
                return scope;
            },
        };

        try
        {
            var act = () =>
            {
                using var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, TimeSpan.FromMilliseconds(50));
            };
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            release.Set();
        }

        (await Task.Run(() => platform.Worker!.Join(Budget), CancellationToken.None)).Should().BeTrue();
        scope.DisposeCount.Should().Be(1);
        scope.DisposeApartment.Should().Be(ApartmentState.STA);
        platform.Events.Should().Equal("ole-init", "ole-uninit");
    }

    [Fact]
    public async Task SelfDispose_KeepsScopeAliveUntilActiveWorkCompletes_AndDisposesOnceAsync()
    {
        var scope = new TrackingScope();
        var disposedInsideWorkAfterSelfDispose = true;
        var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { CreateThreadScope = () => scope });
        try
        {
            var active = scheduler.RunAsync(
                () =>
                {
                    scheduler.Dispose();
                    disposedInsideWorkAfterSelfDispose = scope.DisposeCount > 0;
                    return 5;
                },
                CancellationToken.None);

            _ = await Record.ExceptionAsync(async () => await active.TimeoutAfterAsync(Budget, CancellationToken.None));
            scheduler.Dispose();

            disposedInsideWorkAfterSelfDispose.Should().BeFalse();
            scope.DisposeCount.Should().Be(1);
        }
        finally
        {
            scheduler.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentRepeatedDispose_WithQueuedWork_CompletesAllTasksAndDisposesScopeOnceAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scope = new TrackingScope();
        var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { CreateThreadScope = () => scope });

        try
        {
            var active = scheduler.RunAsync(
                () =>
                {
                    started.TrySetResult(Environment.CurrentManagedThreadId);
                    release.Wait(Budget, CancellationToken.None);
                    return scope.DisposeCount;
                },
                CancellationToken.None);

            var workerThreadId = await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var queued = new Task<int>[6];
            for (var i = 0; i < queued.Length; i++)
            {
                queued[i] = scheduler.RunAsync(() => 1, CancellationToken.None);
            }

            var disposers = new Task[6];
            for (var i = 0; i < disposers.Length; i++)
            {
                disposers[i] = Task.Run(scheduler.Dispose, CancellationToken.None);
            }

            SpinWait.SpinUntil(() => scheduler.PendingWorkItemCount == 0, Budget).Should().BeTrue();
            release.Set();
            await Task.WhenAll(disposers).TimeoutAfterAsync(Budget, CancellationToken.None);

            _ = await Record.ExceptionAsync(async () => await active);
            foreach (var item in queued)
            {
                item.IsCompleted.Should().BeTrue();
                item.IsCanceled.Should().BeTrue();
            }

            scope.DisposeCount.Should().Be(1);
            scope.DisposeThreadId.Should().Be(workerThreadId);
        }
        finally
        {
            release.Set();
            scheduler.Dispose();
        }
    }

    [Fact]
    public async Task ScopeDisposalException_FaultsCompletion_PreservesCanceledPendingWork_AndStillRunsRemainingTeardownAsync()
    {
        using var release = new ManualResetEventSlim(initialState: false);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeFailure = new InvalidOperationException("scope teardown failed");
        var platform = new ScopeRecordingPlatform();
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () => new TrackingScope { DisposeFailure = disposeFailure },
            FreeUnusedComLibrariesOnShutdown = true,
            PostWorkItemMessagePumpBatchSize = 8,
        };

        var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget);
        try
        {
            var active = scheduler.RunAsync(
                () =>
                {
                    started.TrySetResult(true);
                    release.Wait(Budget, CancellationToken.None);
                    return 10;
                },
                CancellationToken.None);
            await started.Task.TimeoutAfterAsync(Budget, CancellationToken.None);
            var pending = scheduler.RunAsync(() => 20, CancellationToken.None);

            var shutdownTask = scheduler.ShutdownAsync(Budget, CancellationToken.None);
            release.Set();

            var shutdownError = await Record.ExceptionAsync(async () => await shutdownTask);
            shutdownError.Should().BeSameAs(disposeFailure);
            (await Record.ExceptionAsync(async () => await scheduler.Completion)).Should().BeSameAs(disposeFailure);

            _ = await Record.ExceptionAsync(async () => await active);
            pending.IsCanceled.Should().BeTrue();

            var disposeAct = scheduler.Dispose;
            disposeAct.Should().NotThrow();

            platform.Events.Should().ContainInOrder("ole-init", "free-libs", "pump:True:8", "ole-uninit");
        }
        finally
        {
            release.Set();
            scheduler.Dispose();
        }
    }

    private sealed class TrackingScope : IDisposable
    {
        private int _disposeCount;
        private int _disposeThreadId = -1;
        private int _disposeApartment = (int)ApartmentState.Unknown;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int DisposeThreadId => Volatile.Read(ref _disposeThreadId);

        public ApartmentState DisposeApartment => (ApartmentState)Volatile.Read(ref _disposeApartment);

        public Exception? DisposeFailure { get; set; }

#pragma warning disable S3877 // Deliberately testing scope disposal exceptions.
        public void Dispose()
        {
            Volatile.Write(ref _disposeThreadId, Environment.CurrentManagedThreadId);
            Volatile.Write(ref _disposeApartment, (int)Thread.CurrentThread.GetApartmentState());
            Interlocked.Increment(ref _disposeCount);
            if (DisposeFailure is not null)
            {
                throw DisposeFailure;
            }
        }
#pragma warning restore S3877
    }

    private sealed class ScopeRecordingPlatform : StaPlatform
    {
        public List<string> Events { get; } = [];

        public Thread? Worker { get; private set; }

        public override void Start(Thread thread)
        {
            Worker = thread;
            base.Start(thread);
        }

        public override void Initialize()
        {
            Events.Add("ole-init");
            base.Initialize();
        }

        public override void FreeUnusedLibraries()
        {
            Events.Add("free-libs");
            base.FreeUnusedLibraries();
        }

        public override PumpOutcome Pump(bool preserveQuit, int maxMessages)
        {
            Events.Add($"pump:{preserveQuit}:{maxMessages}");
            return base.Pump(preserveQuit, maxMessages);
        }

        public override void Uninitialize()
        {
            Events.Add("ole-uninit");
            base.Uninitialize();
        }
    }
}
