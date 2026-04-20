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
public class MutexHelperTest
{
    [Fact]
    public async Task RunInMutex_EnforcesMutualExclusionAsync()
    {
#if NET8_0_OR_GREATER
        var ct = TestContext.Current.CancellationToken;
#else
        var ct = CancellationToken.None;
#endif

        var name = "test_mutex_" + Guid.NewGuid();
        var counter = 0;

        T Run<T>(Func<T> f) => MutexHelper.RunInMutex(name, TimeSpan.FromSeconds(5), isGlobal: false, f);

        var t1 = Task.Run(
            () => Run(() =>
            {
                var before = counter;
                var snapshot = before + 1;
#pragma warning disable S2925
                Thread.Sleep(30);
#pragma warning restore S2925
                counter = snapshot;
                return 0;
            }),
            ct);

        var t2 = Task.Run(
            () => Run(() =>
            {
                var before = counter;
                var snapshot = before + 1;
#pragma warning disable S2925
                Thread.Sleep(30);
#pragma warning restore S2925
                counter = snapshot;
                return 0;
            }),
            ct);

        await Task.WhenAll(t1, t2);
        counter.Should().Be(2);
    }
}
