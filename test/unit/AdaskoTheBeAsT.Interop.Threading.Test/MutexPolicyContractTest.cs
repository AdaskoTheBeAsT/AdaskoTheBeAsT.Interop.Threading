using System;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class MutexPolicyContractTest
{
    [Theory]
    [InlineData("")]
    [InlineData("Global\\qualified")]
    [InlineData("Local\\qualified")]
    public void InvalidName_DoesNotExecute(string name)
    {
        var invoked = false;
        var act = () => MutexHelper.RunInMutex(name, new MutexExecutionOptions(), () => invoked = true, CancellationToken.None);
        act.Should().Throw<ArgumentException>();
        invoked.Should().BeFalse();
    }

    [Fact]
    public void CurrentUserPolicy_DoesNotGrantEveryoneFullControl()
    {
        var name = Guid.NewGuid().ToString("N");
        MutexHelper.RunInMutex(
            name,
            new MutexExecutionOptions(),
            () =>
            {
                using var mutex = MutexAcl.OpenExisting(name, MutexRights.ReadPermissions);
                var rules = mutex.GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
                var world = new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null);
                foreach (MutexAccessRule rule in rules)
                {
                    rule.IdentityReference.Should().NotBe(world);
                    rule.MutexRights.Should().Be(MutexRights.Synchronize | MutexRights.Modify);
                }

                return 0;
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task CanceledAcquisition_DoesNotInvokeDelegateOrRetainOwnershipAsync()
    {
        var name = Guid.NewGuid().ToString("N");
        using var release = new ManualResetEventSlim(initialState: false);
        using var cancellation = new CancellationTokenSource();
        var acquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = Task.Run(
            () => MutexHelper.RunInMutex(
                name,
                new MutexExecutionOptions(),
                () =>
                {
                    acquired.TrySetResult(true);
                    release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                    return 0;
                },
                CancellationToken.None),
            CancellationToken.None);
        try
        {
            await acquired.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invoked = false;
            var contender = Task.Run(
                () =>
                {
                    waiting.TrySetResult(true);
                    return MutexHelper.RunInMutex(name, new MutexExecutionOptions(), () => invoked = true, cancellation.Token);
                },
                CancellationToken.None);
            await waiting.Task.TimeoutAfterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
#if NET8_0_OR_GREATER
            await cancellation.CancelAsync();
#else
            cancellation.Cancel();
#endif
#pragma warning disable VSTHRD003 // Observe the acquisition task under test.
            var error = await Record.ExceptionAsync(async () => await contender);
#pragma warning restore VSTHRD003
            error.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
            invoked.Should().BeFalse();
        }
        finally
        {
            release.Set();
            await owner;
        }

        MutexHelper.RunInMutex(name, new MutexExecutionOptions { Timeout = TimeSpan.Zero }, () => 42, CancellationToken.None)
            .Should().Be(42);
    }
}
