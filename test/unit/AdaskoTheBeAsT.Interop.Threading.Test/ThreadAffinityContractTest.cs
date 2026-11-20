using System;
using System.Globalization;
using System.Threading;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

[Trait("Category", "Portable")]
public class ThreadAffinityContractTest
{
    [Fact]
    public void DefaultInstance_IsUninitialized_AndFailsClosed()
    {
        var uninitialized = default(ThreadAffinity);

        uninitialized.OwningThreadId.Should().Be(0);
        uninitialized.IsInitialized.Should().BeFalse();
        uninitialized.IsCurrentThread.Should().BeFalse();
        uninitialized.ToString().Should().Be("ThreadAffinity(Uninitialized)");

        var defaultOwnerAct = () => uninitialized.ThrowIfWrongThread();
        defaultOwnerAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*ThreadAffinity*not captured*");

        var namedOwnerAct = () => uninitialized.ThrowIfWrongThread("CalculatorSession");
        namedOwnerAct.Should().Throw<InvalidOperationException>()
            .WithMessage("*CalculatorSession*not captured*");
    }

    [Fact]
    public void Capture_BindsToCurrentManagedThread_AndAllowsSameThreadAccess()
    {
        var currentId = Environment.CurrentManagedThreadId;
        var affinity = ThreadAffinity.Capture();

        affinity.OwningThreadId.Should().Be(currentId);
        affinity.IsInitialized.Should().BeTrue();
        affinity.IsCurrentThread.Should().BeTrue();
        affinity.ToString().Should().Be($"ThreadAffinity(ThreadId={currentId})");

        var act = () => affinity.ThrowIfWrongThread("WorkerSession");
        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfWrongThread_ThrowsFromDifferentThread_WithOwnerDetails()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var affinity = ThreadAffinity.Capture();
        var otherThreadId = 0;
        var isCurrentOnOther = true;
        Exception? namedError = null;
        Exception? unnamedError = null;

        var thread = new Thread(
            () =>
            {
                otherThreadId = Environment.CurrentManagedThreadId;
                isCurrentOnOther = affinity.IsCurrentThread;
                namedError = Record.Exception(() => affinity.ThrowIfWrongThread("StatefulComSession"));
                unnamedError = Record.Exception(() => affinity.ThrowIfWrongThread());
            })
        {
            IsBackground = true,
        };
        thread.Start();
        thread.Join();

        otherThreadId.Should().NotBe(ownerThreadId);
        isCurrentOnOther.Should().BeFalse();
        namedError.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("StatefulComSession")
            .And.Contain(ownerThreadId.ToString(CultureInfo.InvariantCulture))
            .And.Contain(otherThreadId.ToString(CultureInfo.InvariantCulture));
        unnamedError.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("Thread-affinitized instance");
    }

    [Fact]
    public void EqualityAndHashCode_CompareOwningThreadId()
    {
        var first = ThreadAffinity.Capture();
        var secondSameThread = ThreadAffinity.Capture();
        ThreadAffinity otherThread = default;

        var thread = new Thread(() => otherThread = ThreadAffinity.Capture()) { IsBackground = true };
        thread.Start();
        thread.Join();

        first.Equals(secondSameThread).Should().BeTrue();
        first.Equals((object)secondSameThread).Should().BeTrue();
        first.Equals((object?)null).Should().BeFalse();
        first.Equals("not-affinity").Should().BeFalse();
        (first == secondSameThread).Should().BeTrue();
        (first != secondSameThread).Should().BeFalse();
        first.GetHashCode().Should().Be(secondSameThread.GetHashCode());

        first.Equals(otherThread).Should().BeFalse();
        (first == otherThread).Should().BeFalse();
        (first != otherThread).Should().BeTrue();
        first.Equals(default).Should().BeFalse();
    }
}
