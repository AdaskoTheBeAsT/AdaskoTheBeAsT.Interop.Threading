using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable VSTHRD003, VSTHRD103, S6966 // Exercising synchronous SingleThreadedApartmentTask.Run is the contract under test.
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaSynchronousExecutionContractTest
{
    [Fact]
    public Task Run_FromMtaThread_ExecutesAllOverloadsOnDedicatedStaThreadAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        return Task.Run(
            () =>
            {
                var callerThreadId = Environment.CurrentManagedThreadId;
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.MTA);

                var actionWorkerId = 0;
                SingleThreadedApartmentTask.Run(
                    () =>
                    {
                        actionWorkerId = Environment.CurrentManagedThreadId;
                        StaApartment.IsCurrentThreadSta(verifyNativeApartment: true).Should().BeTrue();
                    },
                    ct);
                actionWorkerId.Should().NotBe(0).And.NotBe(callerThreadId);

                var yieldActionCalled = false;
                SingleThreadedApartmentTask.Run(
                    yield =>
                    {
                        yield.Occasionally();
                        yieldActionCalled = StaApartment.IsCurrentThreadSta(verifyNativeApartment: true);
                    },
                    ct);
                yieldActionCalled.Should().BeTrue();

                var value = SingleThreadedApartmentTask.Run(
                    () => (Environment.CurrentManagedThreadId, StaApartment.IsCurrentThreadSta(verifyNativeApartment: true)),
                    ct);
                value.CurrentManagedThreadId.Should().NotBe(callerThreadId);
                value.Item2.Should().BeTrue();

                var yieldValue = SingleThreadedApartmentTask.Run(
                    yield =>
                    {
                        yield.Occasionally();
                        return 77;
                    },
                    ct);
                yieldValue.Should().Be(77);

                // ReuseCurrentStaThread from MTA falls back to a dedicated STA thread.
                var fallbackValue = SingleThreadedApartmentTask.Run(
                    () => (Environment.CurrentManagedThreadId, StaApartment.IsCurrentThreadSta(verifyNativeApartment: true)),
                    StaExecutionMode.ReuseCurrentStaThread,
                    ct);
                fallbackValue.CurrentManagedThreadId.Should().NotBe(callerThreadId);
                fallbackValue.Item2.Should().BeTrue();
            },
            ct);
    }

    [Fact]
    public void Run_FromExistingStaThread_HonorsDedicatedVsReuseMode_AndPreservesWmQuit()
    {
        Exception? failure = null;
        var staThread = new Thread(VerifyExistingStaModesAndQuitPreservation) { IsBackground = true };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        failure.Should().BeNull();

        void VerifyExistingStaModesAndQuitPreservation()
        {
            try
            {
                var outerThreadId = Environment.CurrentManagedThreadId;
                SafeNativeMethods.PostQuitMessage(4242);

                var dedicatedThreadId = SingleThreadedApartmentTask.Run(
                    static () => Environment.CurrentManagedThreadId,
                    StaExecutionMode.DedicatedThread);
                dedicatedThreadId.Should().NotBe(outerThreadId);

                var reusedThreadId = 0;
                SingleThreadedApartmentTask.Run(
                    () =>
                    {
                        reusedThreadId = Environment.CurrentManagedThreadId;
                        StaApartment.IsCurrentThreadSta(verifyNativeApartment: true).Should().BeTrue();
                    },
                    StaExecutionMode.ReuseCurrentStaThread);
                reusedThreadId.Should().Be(outerThreadId);

                // ReuseCurrentStaThread must NOT drain or consume the caller's pre-existing WM_QUIT.
                var outcome = NativeMethods.PumpPendingMessages(preserveQuit: false);
                outcome.QuitSeen.Should().BeTrue();
                outcome.ExitCode.Should().Be(4242);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }
    }

    [Fact]
    public void Run_ReuseCurrentStaThreadWithOptions_RestoresBorrowedStaCultureAndLcid()
    {
        Exception? failure = null;
        var staThread = new Thread(VerifyBorrowedThreadCultureRestoration) { IsBackground = true };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        failure.Should().BeNull();

        void VerifyBorrowedThreadCultureRestoration()
        {
            var priorCulture = CultureInfo.CurrentCulture;
            var priorLcid = NativeMethods.GetThreadLocale();
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                _ = NativeMethods.SetThreadLocale(0x0415);

                var observed = SingleThreadedApartmentTask.Run(
                    static () => (CultureInfo.CurrentCulture.Name, NativeMethods.GetThreadLocale()),
                    new SingleThreadedApartmentTaskOptions
                    {
                        ExecutionMode = StaExecutionMode.ReuseCurrentStaThread,
                        ThreadCulture = CultureInfo.InvariantCulture,
                        WindowsThreadLocaleId = 0x0809,
                        ResetFloatingPointMaskBeforeExit = true,
                    });
                observed.Name.Should().Be(string.Empty);
                observed.Item2.Should().Be(0x0809u);

                CultureInfo.CurrentCulture.Name.Should().Be("pl-PL");
                NativeMethods.GetThreadLocale().Should().Be(0x0415u);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                CultureInfo.CurrentCulture = priorCulture;
                _ = NativeMethods.SetThreadLocale(priorLcid);
            }
        }
    }

    [Fact]
    public void Run_PreservesOriginalExceptionTypeAndStackTrace()
    {
        var ct = TestContext.Current.CancellationToken;
        var ex = Record.Exception(() => SingleThreadedApartmentTask.Run<int>(ThrowDistinctHelperException, ct));

        var invalidOp = ex.Should().BeOfType<InvalidOperationException>().Subject;
        invalidOp.Message.Should().Be("distinct-sta-failure");
        invalidOp.StackTrace.Should().Contain(nameof(ThrowDistinctHelperException));
    }

    [Fact]
    public void Run_PreCanceledToken_SkipsBeforeThreadStartAndDelegate()
    {
        var beforeStartCalled = false;
        var delegateCalled = false;
        var canceled = new CancellationToken(canceled: true);

        var act = () => SingleThreadedApartmentTask.Run(
            () => delegateCalled = true,
            new SingleThreadedApartmentTaskOptions
            {
                BeforeThreadStart = () => beforeStartCalled = true,
            },
            canceled);

        act.Should().Throw<OperationCanceledException>()
            .Which.CancellationToken.Should().Be(canceled);
        beforeStartCalled.Should().BeFalse();
        delegateCalled.Should().BeFalse();
    }

    [Fact]
    public async Task MutexHelperAndSynchronousStaRun_NeverReleaseMutexBeforeDedicatedStaThreadExitsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var mutexName = "sta_sync_mutex_" + Guid.NewGuid().ToString("N");
        var options = new MutexExecutionOptions { Timeout = TimeSpan.FromSeconds(10) };
        var workerStillRunning = 0;
        var overlapObserved = false;
        using var enteredSta = new ManualResetEventSlim(initialState: false);
        using var allowStaFinish = new ManualResetEventSlim(initialState: false);
        using var cts = new CancellationTokenSource();

        var firstOwner = Task.Run(
            () => MutexHelper.RunInMutex(
                mutexName,
                options,
                () => SingleThreadedApartmentTask.Run(
                    () =>
                    {
                        Interlocked.Exchange(ref workerStillRunning, 1);
                        enteredSta.Set();
                        allowStaFinish.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
                        Interlocked.Exchange(ref workerStillRunning, 0);
                    },
                    cts.Token),
                CancellationToken.None),
            ct);

        enteredSta.Wait(TimeSpan.FromSeconds(5), ct).Should().BeTrue();
#if NET8_0_OR_GREATER
        await cts.CancelAsync();
#else
        cts.Cancel();
#endif

        var contender = Task.Run(
            () => MutexHelper.RunInMutex(
                mutexName,
                options,
                () =>
                {
                    if (Volatile.Read(ref workerStillRunning) != 0)
                    {
                        overlapObserved = true;
                    }
                },
                ct),
            ct);

#pragma warning disable S2925
        await Task.Delay(50, ct);
#pragma warning restore S2925
        contender.IsCompleted.Should().BeFalse("the first STA thread is still running inside the mutex");
        allowStaFinish.Set();

        var firstError = await Record.ExceptionAsync(async () => await firstOwner);
        firstError.Should().BeAssignableTo<OperationCanceledException>();
        await contender;
        overlapObserved.Should().BeFalse();
    }

    [Fact]
    public void MutexHelper_ActionOverloads_ValidateAndExecute()
    {
        var ct = TestContext.Current.CancellationToken;
        var baseName = "mutex_action_" + Guid.NewGuid().ToString("N");
        var steps = 0;

        MutexHelper.RunInMutex(baseName + "_1", () => steps++);
        MutexHelper.RunInMutex(baseName + "_2", TimeSpan.FromSeconds(5), () => steps++);
        MutexHelper.RunInMutex(baseName + "_3", TimeSpan.FromSeconds(5), isGlobal: false, () => steps++);
        MutexHelper.RunInMutex(baseName + "_4", new MutexExecutionOptions(), () => steps++, ct);
        steps.Should().Be(4);

        const Action nullAction = null!;
        var act1 = () => MutexHelper.RunInMutex(baseName, nullAction);
        act1.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("action");

        var act2 = () => MutexHelper.RunInMutex(baseName, TimeSpan.FromSeconds(1), nullAction);
        act2.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("action");

        var act3 = () => MutexHelper.RunInMutex(baseName, TimeSpan.FromSeconds(1), isGlobal: false, nullAction);
        act3.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("action");

        var act4 = () => MutexHelper.RunInMutex(baseName, new MutexExecutionOptions(), nullAction, ct);
        act4.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("action");

        var actNullOptions = () => MutexHelper.RunInMutex(baseName, null!, static () => { }, ct);
        actNullOptions.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("options");
    }

    [Fact]
    public async Task AsyncActionAndOptionsOverloads_OnSingleThreadedApartmentTask_WorkAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var counter = 0;

        await SingleThreadedApartmentTask.RunAsync(() => Interlocked.Increment(ref counter), ct);
        await SingleThreadedApartmentTask.RunAsync(
            yield =>
            {
                yield.Occasionally();
                Interlocked.Increment(ref counter);
            },
            ct);
        await SingleThreadedApartmentTask.RunWithTimeoutAsync(
            TimeSpan.FromSeconds(5),
            () => Interlocked.Increment(ref counter),
            ct);
        await SingleThreadedApartmentTask.RunAsync(
            () => Interlocked.Increment(ref counter),
            new SingleThreadedApartmentTaskOptions { ThreadName = "Custom-OneOff-STA" },
            ct);
        await SingleThreadedApartmentTask.RunAsync(
            yield =>
            {
                yield.Occasionally();
                Interlocked.Increment(ref counter);
            },
            new SingleThreadedApartmentTaskOptions { ResetFloatingPointMaskBeforeExit = true },
            ct);
        (await SingleThreadedApartmentTask.RunAsync(
            yield =>
            {
                yield.Occasionally();
                return 19;
            },
            new SingleThreadedApartmentTaskOptions { ThreadName = "Custom-Yield-STA" },
            ct)).Should().Be(19);

        counter.Should().Be(5);
    }

    [Fact]
    public void SyncActionAndOptionsOverloads_OnSingleThreadedApartmentTask_Work()
    {
        var ct = TestContext.Current.CancellationToken;
        var counter = 0;

        SingleThreadedApartmentTask.Run(
            () => Interlocked.Increment(ref counter),
            new SingleThreadedApartmentTaskOptions { ThreadName = "Sync-Custom-STA" },
            ct);
        SingleThreadedApartmentTask.Run(
            yield =>
            {
                yield.Occasionally();
                Interlocked.Increment(ref counter);
            },
            new SingleThreadedApartmentTaskOptions { ThreadName = "Sync-Yield-STA" },
            ct);
        SingleThreadedApartmentTask.Run(
            yield =>
            {
                yield.Occasionally();
                return 23;
            },
            new SingleThreadedApartmentTaskOptions { ThreadName = "Sync-FuncYield-STA" },
            ct).Should().Be(23);

        counter.Should().Be(2);

        var invalidModeAct = () => SingleThreadedApartmentTask.Run(static () => { }, (StaExecutionMode)99, ct);
        invalidModeAct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("mode");
    }

    [Fact]
    public async Task SchedulerExtensions_ActionOverloads_WorkAndValidateAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var counter = 0;

        using (var concreteScheduler = new SingleThreadedApartmentTaskScheduler())
        {
            await ((ISingleThreadedApartmentTaskScheduler)concreteScheduler).RunAsync(() => Interlocked.Increment(ref counter), ct);
            await ((ISingleThreadedApartmentTaskScheduler)concreteScheduler).RunAsync(
                () => Interlocked.Increment(ref counter),
                TimeSpan.FromSeconds(5),
                ct);
            await concreteScheduler.RunAsync(() => Interlocked.Increment(ref counter), ct);
        }

        counter.Should().Be(3);

        const ISingleThreadedApartmentTaskScheduler nullScheduler = null!;
        var nullSchedAct = async () => await nullScheduler.RunAsync(static () => { }, ct);
        (await nullSchedAct.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("scheduler");

        using var validScheduler = new SingleThreadedApartmentTaskScheduler();
        var nullActionAct = async () => await validScheduler.RunAsync((Action)null!, TimeSpan.FromSeconds(1), ct);
        (await nullActionAct.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("action");
    }

    [Fact]
    public async Task SingleThreadedApartmentTask_RecoversNanoServerUninitializedCom_AndRejectsMtaOnRunAndRunAsync()
    {
        var syncCalls = 0;
        var syncPlatform = new RecoveryPlatform
        {
            ApartmentHandler = () => ++syncCalls == 1
                ? (NativeMethods.CO_E_NOTINITIALIZED, -1, 0)
                : (0, NativeMethods.APTTYPE_STA, NativeMethods.APTTYPEQUALIFIER_NONE),
        };
        SingleThreadedApartmentTask.Run(static () => 55, options: null, syncPlatform, CancellationToken.None).Should().Be(55);
        syncPlatform.Events.Should().Equal("ole-init", "get-apt", "com-init", "get-apt", "pump", "com-uninit", "ole-uninit");

        var asyncCalls = 0;
        var asyncPlatform = new RecoveryPlatform
        {
            ApartmentHandler = () => ++asyncCalls == 1
                ? (NativeMethods.CO_E_NOTINITIALIZED, -1, 0)
                : (0, NativeMethods.APTTYPE_STA, NativeMethods.APTTYPEQUALIFIER_NONE),
        };
        (await SingleThreadedApartmentTask.RunAsync(static () => 88, options: null, asyncPlatform, CancellationToken.None)).Should().Be(88);
        asyncPlatform.Events.Should().Equal("ole-init", "get-apt", "com-init", "get-apt", "pump", "com-uninit", "ole-uninit");

        var mtaPlatform = new RecoveryPlatform
        {
            ApartmentHandler = static () => (0, NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_IMPLICIT_MTA),
        };
        var mtaSync = () => SingleThreadedApartmentTask.Run(static () => 1, options: null, mtaPlatform, CancellationToken.None);
        mtaSync.Should().Throw<InvalidOperationException>().WithMessage("*BeforeThreadStart*");
        mtaPlatform.Events.Should().Equal("ole-init", "get-apt", "pump", "ole-uninit");

        var skipVerifyPlatform = new RecoveryPlatform
        {
            ApartmentHandler = static () => (0, NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_IMPLICIT_MTA),
        };
        SingleThreadedApartmentTask.Run(
            static () => 9,
            new SingleThreadedApartmentTaskOptions { VerifyNativeStaApartment = false },
            skipVerifyPlatform,
            CancellationToken.None).Should().Be(9);
        skipVerifyPlatform.Events.Should().NotContain("get-apt");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowDistinctHelperException()
    {
        throw new InvalidOperationException("distinct-sta-failure");
    }

    private static class SafeNativeMethods
    {
#pragma warning disable SYSLIB1054 // Shared across Framework and modern .NET test targets.
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern void PostQuitMessage(int exitCode);
#pragma warning restore SYSLIB1054
    }

    private sealed class RecoveryPlatform : StaPlatform
    {
        public List<string> Events { get; } = [];

        public Func<(int Hr, int AptType, int Qualifier)>? ApartmentHandler { get; set; }

        public override void Initialize()
        {
            Events.Add("ole-init");
            base.Initialize();
        }

        public override void Uninitialize()
        {
            Events.Add("ole-uninit");
            base.Uninitialize();
        }

        public override int GetApartmentType(out int apartmentType, out int apartmentQualifier)
        {
            Events.Add("get-apt");
            if (ApartmentHandler is not null)
            {
                (var hr, apartmentType, apartmentQualifier) = ApartmentHandler();
                return hr;
            }

            return base.GetApartmentType(out apartmentType, out apartmentQualifier);
        }

        public override int InitializeComSta()
        {
            Events.Add("com-init");
            return 0;
        }

        public override void UninitializeCom()
        {
            Events.Add("com-uninit");
        }

        public override PumpOutcome Pump(bool preserveQuit)
        {
            Events.Add("pump");
            return base.Pump(preserveQuit);
        }
    }
}
