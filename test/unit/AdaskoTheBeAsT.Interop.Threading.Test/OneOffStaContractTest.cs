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

#pragma warning disable VSTHRD003 // These tests observe the explicitly owned one-off task.
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class OneOffStaContractTest
{
    [Fact]
    public async Task Completion_IncludesOleAndCleanupOnOneThreadAsync()
    {
        var platform = new RecordingPlatform();
        var task = SingleThreadedApartmentTask.RunAsync(
            () =>
            {
                platform.Record("delegate");
                return 42;
            },
            CancellationToken.None,
            platform);
        (await task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(42);
        platform.Calls.Should().Equal("initialize", "delegate", "pump", "uninitialize");
        platform.Threads.Should().OnlyContain(id => id == platform.Threads[0]);
    }

    [Fact]
    public async Task DelegateFailure_WinsOverCleanupFailureAsync()
    {
        var original = new InvalidOperationException("delegate failure");
        var platform = new RecordingPlatform { CleanupFailure = new InvalidOperationException("cleanup failure") };
        var task = SingleThreadedApartmentTask.RunAsync<int>(() => throw original, CancellationToken.None, platform);
        (await Record.ExceptionAsync(async () => await task)).Should().BeSameAs(original);
        platform.Calls.Should().Equal("initialize", "pump", "uninitialize");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailure_FaultsSuccessfulWorkAsync(bool failUninitialize)
    {
        var original = new InvalidOperationException("cleanup failure");
        var platform = new RecordingPlatform
        {
            CleanupFailure = failUninitialize ? null : original,
            UninitializeFailure = failUninitialize ? original : null,
        };
        var task = SingleThreadedApartmentTask.RunAsync(() => 42, CancellationToken.None, platform);
        (await Record.ExceptionAsync(async () => await task)).Should().BeSameAs(original);
        task.IsFaulted.Should().BeTrue();
        platform.Calls.Should().Equal("initialize", "pump", "uninitialize");
    }

    [Fact]
    public async Task InitializationFailure_DoesNotInvokeDelegateOrUninitializeAsync()
    {
        var original = new InvalidOperationException("initialization failure");
        var platform = new RecordingPlatform { InitializationFailure = original };
        var invoked = false;
        var task = SingleThreadedApartmentTask.RunAsync(() => invoked = true, CancellationToken.None, platform);
        (await Record.ExceptionAsync(async () => await task)).Should().BeSameAs(original);
        invoked.Should().BeFalse();
        platform.Calls.Should().Equal("initialize");
    }

    [Fact]
    public async Task UnrelatedCancellation_IsFaulted_NotCanceledAsync()
    {
        var original = new OperationCanceledException(new CancellationToken(canceled: true));
        var task = SingleThreadedApartmentTask.RunAsync<int>(() => throw original, CancellationToken.None);
        (await Record.ExceptionAsync(async () => await task)).Should().BeSameAs(original);
        task.IsFaulted.Should().BeTrue();
    }

    private sealed class RecordingPlatform : StaPlatform
    {
        public IList<string> Calls { get; } = new List<string>();

        public IList<int> Threads { get; } = new List<int>();

        public Exception? InitializationFailure { get; set; }

        public Exception? CleanupFailure { get; set; }

        public Exception? UninitializeFailure { get; set; }

        public override void Initialize()
        {
            Record("initialize");
            if (InitializationFailure is not null)
            {
                throw InitializationFailure;
            }

            base.Initialize();
        }

        public override PumpOutcome Pump(bool preserveQuit)
        {
            Record("pump");
            if (CleanupFailure is not null)
            {
                throw CleanupFailure;
            }

            return base.Pump(preserveQuit);
        }

        public override void Uninitialize()
        {
            Record("uninitialize");
            base.Uninitialize();
            if (UninitializeFailure is not null)
            {
                throw UninitializeFailure;
            }
        }

        public void Record(string call)
        {
            Calls.Add(call);
            Threads.Add(Thread.CurrentThread.ManagedThreadId);
            Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
        }
    }
}
