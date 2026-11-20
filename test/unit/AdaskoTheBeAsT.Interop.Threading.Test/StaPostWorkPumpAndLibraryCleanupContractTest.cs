using System;
using System.Collections.Generic;
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

#pragma warning disable AsyncFixer04, CA2000, IDISP001, IDISP003, IDISP004, VSTHRD003
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaPostWorkPumpAndLibraryCleanupContractTest
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Fact]
    public void Options_DefaultToOptInZeroAndFalse_AndRejectNegativeBatchSize()
    {
        var defaults = new SingleThreadedApartmentTaskSchedulerOptions();
        defaults.BeforeThreadStart.Should().BeNull();
        defaults.VerifyNativeStaApartment.Should().BeTrue();
        defaults.CreateThreadScope.Should().BeNull();
        defaults.PostWorkItemMessagePumpBatchSize.Should().Be(0);
        defaults.FreeUnusedComLibrariesOnShutdown.Should().BeFalse();
        defaults.ResetFloatingPointMaskAfterWorkItem.Should().BeFalse();

        var act = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions { PostWorkItemMessagePumpBatchSize = -1 });
        };

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("options");
    }

    [Fact]
    public async Task PostWorkItemMessagePumpBatchSize_DrainsBoundedBatchWithoutUnboundedReentrancyAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { PostWorkItemMessagePumpBatchSize = 5 });

        var window = await scheduler.RunAsync(() => new MessageWindow(), CancellationToken.None);
        try
        {
            var receivedInsideFirst = await scheduler.RunAsync(
                () =>
                {
                    for (var i = 0; i < 12; i++)
                    {
                        window!.Post();
                    }

                    return window!.Received;
                },
                CancellationToken.None);

            receivedInsideFirst.Should().Be(0);

            var receivedAtSecondStart = await scheduler.RunAsync(() => window!.Received, CancellationToken.None);
            receivedAtSecondStart.Should().BeGreaterThanOrEqualTo(5);
            receivedAtSecondStart.Should().BeLessThanOrEqualTo(5 + AdaskoTheBeAsT.Interop.Threading.NativeMethods.DefaultMessageBatchSize);
        }
        finally
        {
            await scheduler.RunAsync(
                () =>
                {
                    window!.Dispose();
                    return 0;
                },
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task PostWorkItemPump_PreservesWmQuitForSchedulerMainLoopAsync()
    {
        using var scheduler = new SingleThreadedApartmentTaskScheduler(
            new SingleThreadedApartmentTaskSchedulerOptions { PostWorkItemMessagePumpBatchSize = 16 });

        var window = await scheduler.RunAsync(() => new MessageWindow(), CancellationToken.None);
        try
        {
            await scheduler.RunAsync(
                () =>
                {
                    window!.Post();
                    window.Post();
                    NativeMethods.PostQuitMessage(77);
                    return 0;
                },
                CancellationToken.None);

            await scheduler.Completion.TimeoutAfterAsync(Budget, CancellationToken.None);
            window!.Received.Should().Be(2);
            scheduler.QuitExitCode.Should().Be(77);
        }
        finally
        {
            // Window was created on the STA thread; if still undisposed after exit, clean up handle.
            window?.Dispose();
        }
    }

    [Fact]
    public async Task ShutdownTeardownOrder_ScopeDisposeThenFreeLibrariesThenFinalPumpThenUninitializeAsync()
    {
        var events = new List<string>();
        var platform = new TeardownOrderPlatform(events);
        var getCalls = 0;
        platform.GetApartmentHandler = () =>
        {
            getCalls++;
            return getCalls == 1
                ? (AdaskoTheBeAsT.Interop.Threading.NativeMethods.CO_E_NOTINITIALIZED, -1, 0)
                : (0, AdaskoTheBeAsT.Interop.Threading.NativeMethods.APTTYPE_STA, AdaskoTheBeAsT.Interop.Threading.NativeMethods.APTTYPEQUALIFIER_NONE);
        };

        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            CreateThreadScope = () =>
            {
                events.Add("scope-create");
                return new ActionDisposable(() => events.Add("scope-dispose"));
            },
            PostWorkItemMessagePumpBatchSize = 12,
            FreeUnusedComLibrariesOnShutdown = true,
        };

        using (var scheduler = new SingleThreadedApartmentTaskScheduler(options, platform, Budget))
        {
            await scheduler.RunAsync(
                () =>
                {
                    events.Add("work");
                    return 1;
                },
                CancellationToken.None);
        }

        events.Take(3).Should().Equal("ole-init", "com-init-sta", "scope-create");
        var workIndex = events.IndexOf("work");
        workIndex.Should().BeGreaterThanOrEqualTo(3);
        events[workIndex + 1].Should().Be("pump:True:12");

        var teardownIndex = events.IndexOf("scope-dispose");
        teardownIndex.Should().BeGreaterThan(workIndex + 1);
        events.Skip(teardownIndex).Should().Equal(
            "scope-dispose",
            "free-libs",
            "pump:True:12",
            "com-uninit",
            "ole-uninit");
    }

    [Fact]
    public async Task RealWindowsTeardown_ScopePostedMessage_IsDeliveredDuringFinalShutdownPumpAsync()
    {
        MessageWindow? window = null;
        var options = new SingleThreadedApartmentTaskSchedulerOptions
        {
            FreeUnusedComLibrariesOnShutdown = true,
            CreateThreadScope = () =>
            {
                window = new MessageWindow();
                return new ActionDisposable(() => window.Post());
            },
        };

        try
        {
            using (var scheduler = new SingleThreadedApartmentTaskScheduler(options))
            {
                (await scheduler.RunAsync(() => 1, CancellationToken.None)).Should().Be(1);
                window!.Received.Should().Be(0);
            }

            window.Received.Should().Be(1);
        }
        finally
        {
            window?.Dispose();
        }
    }

    private static class NativeMethods
    {
#pragma warning disable SYSLIB1054
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern void PostQuitMessage(int exitCode);
#pragma warning restore SYSLIB1054
    }

    private sealed class ActionDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose?.Invoke();
    }

    private sealed class TeardownOrderPlatform(List<string> events) : StaPlatform
    {
        public Func<(int Hr, int AptType, int Qualifier)>? GetApartmentHandler { get; set; }

        public override void Initialize()
        {
            events.Add("ole-init");
            base.Initialize();
        }

        public override int GetApartmentType(out int apartmentType, out int apartmentQualifier)
        {
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
            events.Add("com-init-sta");
            return 0;
        }

        public override void UninitializeCom()
        {
            events.Add("com-uninit");
        }

        public override void FreeUnusedLibraries()
        {
            events.Add("free-libs");
            base.FreeUnusedLibraries();
        }

        public override PumpOutcome Pump(bool preserveQuit, int maxMessages)
        {
            events.Add($"pump:{preserveQuit}:{maxMessages}");
            return base.Pump(preserveQuit, maxMessages);
        }

        public override void Uninitialize()
        {
            events.Add("ole-uninit");
            base.Uninitialize();
        }
    }
}
