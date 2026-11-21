using System;
using System.Globalization;
using System.Threading;
using AwesomeAssertions;
using Xunit;

namespace AdaskoTheBeAsT.Interop.Threading.Test;

[Trait("Category", "Portable")]
public class ThreadCultureScopePortableContractTest
{
    [Fact]
    public void Enter_AppliesAndRestoresManagedCultures_AndIsIdempotent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        var polish = CultureInfo.GetCultureInfo("pl-PL");
        var french = CultureInfo.GetCultureInfo("fr-FR");
        var invariant = CultureInfo.InvariantCulture;
        var englishUi = CultureInfo.GetCultureInfo("en-US");

        try
        {
            CultureInfo.CurrentCulture = polish;
            CultureInfo.CurrentUICulture = french;

#pragma warning disable IDISP016, IDISP017 // Explicitly testing idempotent double Dispose().
            var scope = ThreadCultureScope.Enter(invariant, englishUi);
            try
            {
                scope.Affinity.IsCurrentThread.Should().BeTrue();
                CultureInfo.CurrentCulture.Should().Be(invariant);
                CultureInfo.CurrentUICulture.Should().Be(englishUi);
                1.5d.ToString(CultureInfo.CurrentCulture).Should().Be("1.5");

                // Poison mid-scope to verify Dispose restores the pre-scope culture even after further mutation.
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            }
            finally
            {
                scope.Dispose();
            }

            CultureInfo.CurrentCulture.Should().Be(polish);
            CultureInfo.CurrentUICulture.Should().Be(french);

            // Second Dispose on owning thread is a safe no-op.
            var secondDispose = () => scope.Dispose();
            secondDispose.Should().NotThrow();
#pragma warning restore IDISP016, IDISP017
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void Dispose_OnWrongThread_ThrowsAndLeavesOwningThreadRestorable()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var scope = ThreadCultureScope.Enter(CultureInfo.InvariantCulture);
        try
        {
            Exception? wrongThreadError = null;
            var thread = new Thread(() => wrongThreadError = Record.Exception(scope.Dispose))
            {
                IsBackground = true,
            };
            thread.Start();
            thread.Join();

            wrongThreadError.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain(nameof(ThreadCultureScope));
        }
        finally
        {
            scope.Dispose();
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
