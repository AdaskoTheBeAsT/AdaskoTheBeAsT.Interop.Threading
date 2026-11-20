using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
public class StaCultureAndLocaleContractTest
{
    private const int EnGbLcid = 0x0809;
    private const int EnUsLcid = 0x0409;
    private const int PlPlLcid = 0x0415;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidWindowsThreadLocaleId_ThrowsArgumentOutOfRangeException(int invalidLcid)
    {
        var schedulerAct = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions { WindowsThreadLocaleId = invalidLcid });
        };
        schedulerAct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("options");

        var oneOffAct = () => SingleThreadedApartmentTask.Run(
            static () => 1,
            new SingleThreadedApartmentTaskOptions { WindowsThreadLocaleId = invalidLcid });
        oneOffAct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("options");

        var scopeAct = () =>
        {
            using var scope = ThreadCultureScope.Enter(CultureInfo.InvariantCulture, uiCulture: null, invalidLcid);
        };
        scopeAct.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("windowsThreadLocaleId");
    }

    [Fact]
    public void SetThreadLocaleFailure_SurfacesWin32ExceptionAtStartupAndEnter()
    {
        var failingPlatform = new LocaleTrackingPlatform
        {
            SetLocaleFailure = new Win32Exception(87, "The parameter is incorrect."),
        };

        var schedulerAct = () =>
        {
            using var scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions { WindowsThreadLocaleId = EnGbLcid },
                failingPlatform,
                Budget);
        };
        schedulerAct.Should().Throw<Win32Exception>().Which.NativeErrorCode.Should().Be(87);

        var originalCulture = CultureInfo.CurrentCulture;
        var polish = CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            CultureInfo.CurrentCulture = polish;
            var scopeAct = () => ThreadCultureScope.Enter(
                CultureInfo.InvariantCulture,
                CultureInfo.GetCultureInfo("en-US"),
                EnGbLcid,
                failingPlatform);
            scopeAct.Should().Throw<Win32Exception>().Which.NativeErrorCode.Should().Be(87);
            CultureInfo.CurrentCulture.Should().Be(polish, "failed Enter must roll back managed culture changes");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task Scheduler_PinsCultureAndWindowsLcidBeforeCreateThreadScope_AndHealsPoisonedItemsAsync()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        (string Formatted, string UiName, uint Lcid) scopeObserved = default;

        try
        {
            // Simulate a host thread running under Polish comma-decimal locale.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            using var scheduler = new SingleThreadedApartmentTaskScheduler(
                new SingleThreadedApartmentTaskSchedulerOptions
                {
                    ThreadCulture = CultureInfo.InvariantCulture,
                    ThreadUICulture = CultureInfo.GetCultureInfo("en-US"),
                    WindowsThreadLocaleId = EnGbLcid,
                    ResetThreadCultureAndLocaleAfterWorkItem = true,
                    CreateThreadScope = () => CaptureAndPoisonInScope(out scopeObserved),
                });

            scopeObserved.Formatted.Should().Be("1234.5");
            scopeObserved.UiName.Should().Be("en-US");
            scopeObserved.Lcid.Should().Be((uint)EnGbLcid);

            var firstObserved = await scheduler.RunAsync(CaptureAndPoisonInWorkItem, CancellationToken.None);
            firstObserved.Formatted.Should().Be("98.75");
            firstObserved.UiName.Should().Be("en-US");
            firstObserved.Lcid.Should().Be((uint)EnGbLcid);

            var secondObserved = await scheduler.RunAsync(
                static () => (
                    Formatted: 98.75d.ToString(CultureInfo.CurrentCulture),
                    UiName: CultureInfo.CurrentUICulture.Name,
                    Lcid: NativeMethods.GetThreadLocale()),
                CancellationToken.None);

            secondObserved.Formatted.Should().Be("98.75");
            secondObserved.UiName.Should().Be("en-US");
            secondObserved.Lcid.Should().Be((uint)EnGbLcid);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public async Task ResetAfterWorkItem_SkipsRedundantSetThreadLocaleWhenUnmutatedAsync()
    {
        var platform = new LocaleTrackingPlatform { CurrentLcid = EnUsLcid };
        using (var scheduler = new SingleThreadedApartmentTaskScheduler(
                   new SingleThreadedApartmentTaskSchedulerOptions
                   {
                       ThreadCulture = CultureInfo.InvariantCulture,
                       WindowsThreadLocaleId = EnGbLcid,
                       ResetThreadCultureAndLocaleAfterWorkItem = true,
                   },
                   platform,
                   Budget))
        {
            // Clean work item does not touch LCID -> SetThreadLocale must not be re-invoked.
            (await scheduler.RunAsync(() => 1, CancellationToken.None)).Should().Be(1);
            platform.SetLocaleCalls.Should().Equal((uint)EnGbLcid);

            // Poisoning work item mutates simulated LCID -> finally restores EnGbLcid.
            await scheduler.RunAsync(
                () =>
                {
                    platform.CurrentLcid = PlPlLcid;
                    return 2;
                },
                CancellationToken.None);
            platform.SetLocaleCalls.Should().Equal((uint)EnGbLcid, (uint)EnGbLcid);
        }
    }

    [Fact]
    public void ThreadCultureScope_OnLiveWindowsStaThread_RestoresCultureUiCultureAndWin32Lcid()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var polish = CultureInfo.GetCultureInfo("pl-PL");
                    var french = CultureInfo.GetCultureInfo("fr-FR");
                    CultureInfo.CurrentCulture = polish;
                    CultureInfo.CurrentUICulture = french;
                    NativeMethods.SetThreadLocale(PlPlLcid).Should().BeTrue();

#pragma warning disable IDISP016, IDISP017 // Explicitly testing idempotent double Dispose().
                    var scope = ThreadCultureScope.Enter(
                        CultureInfo.InvariantCulture,
                        CultureInfo.GetCultureInfo("en-US"),
                        EnGbLcid);
                    try
                    {
                        scope.Affinity.IsCurrentThread.Should().BeTrue();
                        CultureInfo.CurrentCulture.Should().Be(CultureInfo.InvariantCulture);
                        CultureInfo.CurrentUICulture.Name.Should().Be("en-US");
                        NativeMethods.GetThreadLocale().Should().Be((uint)EnGbLcid);

                        // Null LCID overload leaves Win32 LCID alone while scoping managed culture.
                        using (ThreadCultureScope.Enter(CultureInfo.GetCultureInfo("en-GB"), uiCulture: null, windowsThreadLocaleId: null))
                        {
                            CultureInfo.CurrentCulture.Name.Should().Be("en-GB");
                            NativeMethods.GetThreadLocale().Should().Be((uint)EnGbLcid);
                        }

                        CultureInfo.CurrentCulture.Should().Be(CultureInfo.InvariantCulture);
                    }
                    finally
                    {
                        scope.Dispose();
                        scope.Dispose();
                    }
#pragma warning restore IDISP016, IDISP017

                    CultureInfo.CurrentCulture.Should().Be(polish);
                    CultureInfo.CurrentUICulture.Should().Be(french);
                    NativeMethods.GetThreadLocale().Should().Be((uint)PlPlLcid);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
    }

    private static IDisposable? CaptureAndPoisonInScope(out (string Formatted, string UiName, uint Lcid) observed)
    {
        observed = (
            1234.5d.ToString(CultureInfo.CurrentCulture),
            CultureInfo.CurrentUICulture.Name,
            NativeMethods.GetThreadLocale());

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        _ = NativeMethods.SetThreadLocale(PlPlLcid);
        return null;
    }

    private static (string Formatted, string UiName, uint Lcid) CaptureAndPoisonInWorkItem()
    {
        var snapshot = (
            Formatted: 98.75d.ToString(CultureInfo.CurrentCulture),
            UiName: CultureInfo.CurrentUICulture.Name,
            Lcid: NativeMethods.GetThreadLocale());

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
        NativeMethods.SetThreadLocale(PlPlLcid).Should().BeTrue();
        return snapshot;
    }

    private sealed class LocaleTrackingPlatform : StaPlatform
    {
        public List<uint> SetLocaleCalls { get; } = [];

        public uint CurrentLcid { get; set; } = EnUsLcid;

        public Exception? SetLocaleFailure { get; set; }

        public override uint GetThreadLocale()
        {
            return CurrentLcid;
        }

        public override void SetThreadLocale(uint localeId)
        {
            if (SetLocaleFailure is not null)
            {
                throw SetLocaleFailure;
            }

            SetLocaleCalls.Add(localeId);
            CurrentLcid = localeId;
        }
    }
}
