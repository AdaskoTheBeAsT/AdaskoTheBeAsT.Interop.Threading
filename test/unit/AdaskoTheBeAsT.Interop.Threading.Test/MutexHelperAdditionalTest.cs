using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class MutexHelperAdditionalTest
{
    [Fact]
    public void RunInMutex_NullFunc_Throws()
    {
        var act = () => MutexHelper.RunInMutex<int>(
            "mutex_null_" + Guid.NewGuid(),
            TimeSpan.FromSeconds(1),
            isGlobal: false,
            func: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RunInMutex_InfiniteTimeout_ReturnsResult()
    {
        var name = "mutex_infinite_" + Guid.NewGuid();

        var result = MutexHelper.RunInMutex(name, () => 77);

        result.Should().Be(77);
    }

    [Fact]
    public void RunInMutex_WithTimeout_Global_ReturnsResult()
    {
        var name = "mutex_timeout_global_" + Guid.NewGuid();

        var result = MutexHelper.RunInMutex(name, TimeSpan.FromSeconds(1), () => 13);

        result.Should().Be(13);
    }

    [Fact]
    public void RunInMutex_WithTimeout_Local_ReturnsResult()
    {
        var name = "mutex_timeout_local_" + Guid.NewGuid();

        var result = MutexHelper.RunInMutex(name, TimeSpan.FromSeconds(1), isGlobal: false, () => 21);

        result.Should().Be(21);
    }

    [Fact]
    public async Task RunInMutex_TimeoutExpires_ThrowsTimeoutExceptionAsync()
    {
        var name = "mutex_timeout_expire_" + Guid.NewGuid();

        using var holderEntered = new ManualResetEventSlim(initialState: false);
        using var releaseHolder = new ManualResetEventSlim(initialState: false);

#if NET8_0_OR_GREATER
        var testCancellationToken = TestContext.Current.CancellationToken;
#else
        var testCancellationToken = CancellationToken.None;
#endif

        var holder = Task.Run(
            () => MutexHelper.RunInMutex<int>(
                name,
                TimeSpan.FromSeconds(10),
                isGlobal: false,
                () =>
                {
                    holderEntered.Set();
                    releaseHolder.Wait(TimeSpan.FromSeconds(10), testCancellationToken);
                    return 0;
                }),
            testCancellationToken);

        holderEntered.Wait(TimeSpan.FromSeconds(5), testCancellationToken).Should().BeTrue();

        // Holder is inside the mutex; this contender has only 100 ms, so it must time out.
        var act = () => MutexHelper.RunInMutex<int>(
            name,
            TimeSpan.FromMilliseconds(100),
            isGlobal: false,
            () => 1);

        act.Should().Throw<TimeoutException>();

        releaseHolder.Set();
        await holder;
    }

    [Fact]
    public void RunInMutex_PropagatesExceptionFromFunc()
    {
        var name = "mutex_exc_" + Guid.NewGuid();

        var act = () => MutexHelper.RunInMutex<int>(
            name,
            TimeSpan.FromSeconds(1),
            isGlobal: false,
            () => throw new InvalidOperationException("boom"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public void RunInMutex_ReleasesMutex_AfterExceptionFromFunc()
    {
        var name = "mutex_release_after_exc_" + Guid.NewGuid();

        // First call throws: it must still release the mutex.
        var firstCall = () => MutexHelper.RunInMutex<int>(
            name,
            TimeSpan.FromSeconds(1),
            isGlobal: false,
            () => throw new InvalidOperationException("first call"));

        firstCall.Should().Throw<InvalidOperationException>();

        // Second call: if the first did not release the mutex, this would time out.
        var result = MutexHelper.RunInMutex<int>(
            name,
            TimeSpan.FromSeconds(1),
            isGlobal: false,
            () => 42);

        result.Should().Be(42);
    }
}
