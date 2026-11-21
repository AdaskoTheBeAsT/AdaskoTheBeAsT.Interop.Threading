using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#pragma warning disable VSTHRD003
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaApartmentAndStartupContractTest
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task BeforeThreadStart_RunsOnCallerBeforeThreadStart_AndCapturesOptionsAsync()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var beforeStartThreadId = -1;
        var platform = new ApartmentTestPlatform();
        SingleThreadedApartmentTaskSchedulerOptions? options = null;
        options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            ThreadName = "Captured-STA",
            MaximumPendingWorkItems = 4,
            PostWorkItemMessagePumpBatchSize = 2,
            BeforeThreadStart = () =>
            {
                beforeStartThreadId = Environment.CurrentManagedThreadId;
                platform.Events.Add("before-thread-start");

                // Mutate every option after construction began; captured values must win.
                options!.ThreadName = "Mutated-STA";
                options.MaximumPendingWorkItems = 1;
                options.PostWorkItemMessagePumpBatchSize = 0;
                options.VerifyNativeStaApartment = false;
                options.CreateThreadScope = static () => throw new InvalidOperationException("Must not run mutated scope.");
                options.FreeUnusedComLibrariesOnShutdown = true;
            },
        };

        using (var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget))
        {
            var observedName = await scheduler.RunAsync(() => Thread.CurrentThread.Name, CancellationToken.None);
            observedName.Should().Be("Captured-STA");
        }

        beforeStartThreadId.Should().Be(callerThreadId);
        platform.Events[0].Should().Be("before-thread-start");
        platform.Events[1].Should().Be("start");
        platform.Events[2].Should().Be("ole-init");
        platform.Events[3].Should().Be("get-apt");
        platform.Events.Should().Contain("pump:True:2");
        platform.Events.Should().NotContain("free-libs");
    }

    [Fact]
    public void BeforeThreadStart_Exception_AbortsBeforeStartingThread()
    {
        var expected = new InvalidOperationException("shim preparation failed");
        var platform = new ApartmentTestPlatform();
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            BeforeThreadStart = () => throw expected,
        };

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget);
        };

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        platform.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultScheduler_VerifiesRealNativeStaApartmentAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler();
        var (hr, aptType) = await scheduler.RunAsync(
            () =>
            {
                var result = NativeMethods.CoGetApartmentType(out var type, out _);
                return (result, type);
            },
            CancellationToken.None);

        hr.Should().Be(0);
        aptType.Should().BeOneOf(NativeMethods.APTTYPE_STA, NativeMethods.APTTYPE_MAINSTA);
    }

    [Fact]
    public async Task UninitializedNativeApartment_RecoversViaCoInitializeEx_AndBalancesOnExitAsync()
    {
        var getCalls = 0;
        var platform = new ApartmentTestPlatform
        {
            GetApartmentHandler = () =>
            {
                getCalls++;
                return getCalls == 1
                    ? (NativeMethods.CO_E_NOTINITIALIZED, -1, 0)
                    : (0, NativeMethods.APTTYPE_STA, NativeMethods.APTTYPEQUALIFIER_NONE);
            },
            InitComStaResult = 0,
        };

        using (var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget))
        {
            (await scheduler.RunAsync(() => 99, CancellationToken.None)).Should().Be(99);
        }

        platform.Events.Should().Contain("pump:False:64");
        platform.Events.Where(static e => !string.Equals(e, "pump:False:64", StringComparison.Ordinal)).Should().Equal(
            "start",
            "ole-init",
            "get-apt",
            "com-init-sta",
            "get-apt",
            "com-uninit",
            "ole-uninit");
    }

    [Fact]
    public void ChangedModeDuringComStaRecovery_PropagatesAndBalancesOnlyOle()
    {
        var platform = new ApartmentTestPlatform
        {
            GetApartmentHandler = static () => (NativeMethods.CO_E_NOTINITIALIZED, -1, 0),
            InitComStaResult = NativeMethods.RPC_E_CHANGED_MODE,
        };

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget);
        };

        act.Should().Throw<COMException>().Which.HResult.Should().Be(NativeMethods.RPC_E_CHANGED_MODE);
        platform.Worker!.Join(Budget).Should().BeTrue();
        platform.Events.Should().Equal("start", "ole-init", "get-apt", "com-init-sta", "ole-uninit");
    }

    [Fact]
    public void ChangedModeDuringOleInitialize_PropagatesWithoutAnyUninitialize()
    {
        var platform = new ApartmentTestPlatform
        {
            OleInitFailure = new COMException("RPC_E_CHANGED_MODE", NativeMethods.RPC_E_CHANGED_MODE),
        };

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget);
        };

        act.Should().Throw<COMException>().Which.HResult.Should().Be(NativeMethods.RPC_E_CHANGED_MODE);
        platform.Worker!.Join(Budget).Should().BeTrue();
        platform.Events.Should().Equal("start", "ole-init");
    }

    [Theory]
    [InlineData(NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_NONE)]
    [InlineData(NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_IMPLICIT_MTA)]
    [InlineData(NativeMethods.APTTYPE_NA, 2)]
    public void MtaNativeApartment_FaultsConstruction_AndBalancesOleOnly(int aptType, int qualifier)
    {
        var scopeCalled = false;
        var platform = new ApartmentTestPlatform
        {
            GetApartmentHandler = () => (0, aptType, qualifier),
        };
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () =>
            {
                scopeCalled = true;
                return null;
            },
        };

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget);
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BeforeThreadStart*");
        scopeCalled.Should().BeFalse();
        platform.Worker!.Join(Budget).Should().BeTrue();
        platform.Events.Should().Equal("start", "ole-init", "get-apt", "ole-uninit");
    }

    [Fact]
    public void MtaAfterComStaRecovery_BalancesBothComAndOle()
    {
        var calls = 0;
        var platform = new ApartmentTestPlatform
        {
            GetApartmentHandler = () =>
            {
                calls++;
                return calls == 1
                    ? (NativeMethods.CO_E_NOTINITIALIZED, -1, 0)
                    : (0, NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_IMPLICIT_MTA);
            },
            InitComStaResult = 0,
        };

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget);
        };

        act.Should().Throw<InvalidOperationException>();
        platform.Worker!.Join(Budget).Should().BeTrue();
        platform.Events.Should().Equal("start", "ole-init", "get-apt", "com-init-sta", "get-apt", "com-uninit", "ole-uninit");
    }

    [Theory]
    [InlineData(NativeMethods.APTTYPE_MAINSTA, NativeMethods.APTTYPEQUALIFIER_NONE)]
    [InlineData(NativeMethods.APTTYPE_NA, NativeMethods.APTTYPEQUALIFIER_NA_ON_STA)]
    [InlineData(NativeMethods.APTTYPE_NA, NativeMethods.APTTYPEQUALIFIER_NA_ON_MAINSTA)]
    public async Task StaHostedApartments_AreAcceptedAsync(int aptType, int qualifier)
    {
        var platform = new ApartmentTestPlatform
        {
            GetApartmentHandler = () => (0, aptType, qualifier),
        };

        using var scheduler = new SingleThreadedApartmentTaskScheduler(options: null, platform, Budget);
        (await scheduler.RunAsync(() => 7, CancellationToken.None)).Should().Be(7);
    }

    [Fact]
    public async Task VerifyNativeStaApartmentFalse_SkipsNativeApartmentQueryAsync()
    {
        var platform = new ApartmentTestPlatform
        {
            GetApartmentHandler = static () => (0, NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_NONE),
        };
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            VerifyNativeStaApartment = false,
        };

        using (var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget))
        {
            (await scheduler.RunAsync(() => 123, CancellationToken.None)).Should().Be(123);
        }

        platform.Events.Should().NotContain("get-apt");
    }

    private sealed class ApartmentTestPlatform : StaPlatform
    {
        public List<string> Events { get; } = [];

        public Func<(int Hr, int AptType, int Qualifier)>? GetApartmentHandler { get; set; }

        public int InitComStaResult { get; set; }

        public Exception? OleInitFailure { get; set; }

        public Thread? Worker { get; private set; }

        public override void Start(Thread thread)
        {
            Events.Add("start");
            Worker = thread;
            base.Start(thread);
        }

        public override void Initialize()
        {
            Events.Add("ole-init");
            if (OleInitFailure is not null)
            {
                throw OleInitFailure;
            }

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
            if (GetApartmentHandler is not null)
            {
                var (hr, type, qualifier) = GetApartmentHandler();
                apartmentType = type;
                apartmentQualifier = qualifier;
                return hr;
            }

            return base.GetApartmentType(out apartmentType, out apartmentQualifier);
        }

        public override int InitializeComSta()
        {
            Events.Add("com-init-sta");
            return InitComStaResult;
        }

        public override void UninitializeCom()
        {
            Events.Add("com-uninit");
        }

        public override void FreeUnusedLibraries()
        {
            Events.Add("free-libs");
        }

        public override PumpOutcome Pump(bool preserveQuit, int maxMessages)
        {
            Events.Add($"pump:{preserveQuit}:{maxMessages.ToString(CultureInfo.InvariantCulture)}");
            return base.Pump(preserveQuit, maxMessages);
        }
    }
}
