using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class StaApartmentScopeContractTest
{
    [Fact]
    public Task MtaThread_RejectsIsVerifyAndEnterScopeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        return Task.Run(
            () =>
            {
                Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.MTA);
                StaApartment.IsCurrentThreadSta(verifyNativeApartment: false).Should().BeFalse();
                StaApartment.IsCurrentThreadSta(verifyNativeApartment: true).Should().BeFalse();

                var verifyFalse = () => StaApartment.VerifyCurrentThreadSta(verifyNativeApartment: false);
                verifyFalse.Should().Throw<InvalidOperationException>();

                var verifyTrue = () => StaApartment.VerifyCurrentThreadSta(verifyNativeApartment: true);
                verifyTrue.Should().Throw<InvalidOperationException>();

                var enterCom = () => StaApartment.EnterScope(initializeOle: false);
                enterCom.Should().Throw<InvalidOperationException>();

                var enterOle = () => StaApartment.EnterScope(initializeOle: true);
                enterOle.Should().Throw<InvalidOperationException>();
            },
            ct);
    }

    [Fact]
    public void LiveStaThread_EnterScope_RegistersComAndOle_SupportsIdempotentDispose_AndRejectsWrongThreadDispose()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    StaApartment.IsCurrentThreadSta(verifyNativeApartment: false).Should().BeTrue();
                    StaApartment.VerifyCurrentThreadSta(verifyNativeApartment: false);

                    using (var comScope = StaApartment.EnterScope(initializeOle: false))
                    {
                        comScope.Affinity.IsCurrentThread.Should().BeTrue();
                        StaApartment.IsCurrentThreadSta(verifyNativeApartment: true).Should().BeTrue();
                        StaApartment.VerifyCurrentThreadSta(verifyNativeApartment: true);

#pragma warning disable IDISP016, IDISP017 // Testing wrong-thread and idempotent repeated Dispose().
                        var oleScope = StaApartment.EnterScope(initializeOle: true);
                        try
                        {
                            oleScope.Affinity.OwningThreadId.Should().Be(Environment.CurrentManagedThreadId);
                            StaApartment.IsCurrentThreadSta(verifyNativeApartment: true).Should().BeTrue();

                            Exception? crossThreadError = null;
                            var wrongThread = new Thread(() => crossThreadError = Record.Exception(oleScope.Dispose))
                            {
                                IsBackground = true,
                            };
                            wrongThread.Start();
                            wrongThread.Join();

                            crossThreadError.Should().BeOfType<InvalidOperationException>()
                                .Which.Message.Should().Contain(nameof(StaApartmentScope));
                        }
                        finally
                        {
                            oleScope.Dispose();
                            oleScope.Dispose();
                        }
#pragma warning restore IDISP016, IDISP017
                    }
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

    [Fact]
    public void SimulatedPlatform_VerifyCurrentThreadSta_HandlesUninitializedErrorAndMta()
    {
        var uninitPlatform = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            ApartmentResult = (NativeMethods.CO_E_NOTINITIALIZED, -1, 0),
        };
        StaApartment.IsCurrentThreadSta(uninitPlatform, verifyNativeApartment: true).Should().BeFalse();
        var uninitAct = () => StaApartment.VerifyCurrentThreadSta(uninitPlatform, verifyNativeApartment: true);
        uninitAct.Should().Throw<InvalidOperationException>()
            .WithInnerException<COMException>()
            .Which.HResult.Should().Be(NativeMethods.CO_E_NOTINITIALIZED);

        var failPlatform = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            ApartmentResult = (unchecked((int)0x8007000E), -1, 0),
        };
        var failAct = () => StaApartment.VerifyCurrentThreadSta(failPlatform, verifyNativeApartment: true);
        failAct.Should().Throw<InvalidOperationException>()
            .WithInnerException<COMException>()
            .Which.HResult.Should().Be(unchecked((int)0x8007000E));

        var mtaPlatform = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            ApartmentResult = (0, NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_IMPLICIT_MTA),
        };
        var mtaAct = () => StaApartment.VerifyCurrentThreadSta(mtaPlatform, verifyNativeApartment: true);
        mtaAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*instead of STA*");
    }

    [Fact]
    public void SimulatedPlatform_EnterScope_RejectsChangedModeAndComInitFailure()
    {
        var changedMode = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            InitComResult = NativeMethods.RPC_E_CHANGED_MODE,
        };
        var changedAct = () => StaApartment.EnterScope(changedMode, initializeOle: false);
        changedAct.Should().Throw<InvalidOperationException>()
            .WithInnerException<COMException>()
            .Which.HResult.Should().Be(NativeMethods.RPC_E_CHANGED_MODE);
        changedMode.Events.Should().Equal("com-init");

        var comFail = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            InitComResult = unchecked((int)0x8007000E),
        };
        var comFailAct = () => StaApartment.EnterScope(comFail, initializeOle: false);
        comFailAct.Should().Throw<COMException>()
            .Which.HResult.Should().Be(unchecked((int)0x8007000E));
        comFail.Events.Should().Equal("com-init");
    }

    [Fact]
    public void SimulatedPlatform_EnterScope_RollsBackOnOleFailureAndMtaAndUnknownApartment()
    {
        var oleFail = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            InitComResult = 0,
            OleFailure = new COMException("OleInitialize failed", unchecked((int)0x8007000E)),
        };
        var oleFailAct = () => StaApartment.EnterScope(oleFail, initializeOle: true);
        oleFailAct.Should().Throw<COMException>();
        oleFail.Events.Should().Equal("com-init", "ole-init", "com-uninit");

        var mtaAfterOle = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.STA,
            InitComResult = 1,
            ApartmentResult = (0, NativeMethods.APTTYPE_MTA, NativeMethods.APTTYPEQUALIFIER_NONE),
        };
        var mtaAfterOleAct = () => StaApartment.EnterScope(mtaAfterOle, initializeOle: true);
        mtaAfterOleAct.Should().Throw<InvalidOperationException>();
        mtaAfterOle.Events.Should().Equal("com-init", "ole-init", "get-apt", "ole-uninit", "com-uninit");

        var unknownCannotSetSta = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.Unknown,
            TrySetStaResult = false,
        };
        var unknownAct = () => StaApartment.EnterScope(unknownCannotSetSta, initializeOle: false);
        unknownAct.Should().Throw<InvalidOperationException>();
        unknownCannotSetSta.Events.Should().BeEmpty();

        var balanced = new SimulatedApartmentPlatform
        {
            ManagedState = ApartmentState.Unknown,
            TrySetStaResult = true,
            InitComResult = 0,
            ApartmentResult = (0, NativeMethods.APTTYPE_STA, NativeMethods.APTTYPEQUALIFIER_NONE),
        };
        using (var scope = StaApartment.EnterScope(balanced, initializeOle: true))
        {
            scope.Affinity.IsCurrentThread.Should().BeTrue();
        }

        balanced.Events.Should().Equal("com-init", "ole-init", "get-apt", "ole-uninit", "com-uninit");
    }

    private sealed class SimulatedApartmentPlatform : StaPlatform
    {
        public List<string> Events { get; } = [];

        public ApartmentState ManagedState { get; set; } = ApartmentState.STA;

        public bool TrySetStaResult { get; set; } = true;

        public int InitComResult { get; set; }

        public Exception? OleFailure { get; set; }

        public (int Hr, int AptType, int Qualifier) ApartmentResult { get; set; } =
            (0, NativeMethods.APTTYPE_STA, NativeMethods.APTTYPEQUALIFIER_NONE);

        public override ApartmentState GetManagedApartmentState()
        {
            return ManagedState;
        }

        public override bool TryEnsureManagedStaState()
        {
            if (TrySetStaResult)
            {
                ManagedState = ApartmentState.STA;
            }

            return TrySetStaResult;
        }

        public override int InitializeComSta()
        {
            Events.Add("com-init");
            return InitComResult;
        }

        public override void UninitializeCom()
        {
            Events.Add("com-uninit");
        }

        public override void Initialize()
        {
            Events.Add("ole-init");
            if (OleFailure is not null)
            {
                throw OleFailure;
            }
        }

        public override void Uninitialize()
        {
            Events.Add("ole-uninit");
        }

        public override int GetApartmentType(out int apartmentType, out int apartmentQualifier)
        {
            Events.Add("get-apt");
            (var hr, apartmentType, apartmentQualifier) = ApartmentResult;
            return hr;
        }
    }
}
