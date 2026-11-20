using System;

namespace AdaskoTheBeAsT.Interop.Threading;

/// <summary>
/// Captures a managed thread identifier and verifies that subsequent operations run on that exact thread.
/// </summary>
/// <remarks>
/// <c>default(ThreadAffinity)</c> represents an uninitialized guard (<see cref="OwningThreadId"/> is <c>0</c>).
/// Because <see cref="Environment.CurrentManagedThreadId"/> is strictly positive, an uninitialized guard
/// never matches the calling thread and throws <see cref="InvalidOperationException"/> from <see cref="ThrowIfWrongThread"/>.
/// </remarks>
public readonly struct ThreadAffinity : IEquatable<ThreadAffinity>
{
    private ThreadAffinity(int owningThreadId)
    {
        OwningThreadId = owningThreadId;
    }

    /// <summary>
    /// Gets the captured managed thread identifier, or <c>0</c> when this instance was not initialized via <see cref="Capture"/>.
    /// </summary>
    public int OwningThreadId { get; }

    /// <summary>
    /// Gets a value indicating whether an owning managed thread identifier has been captured.
    /// </summary>
    public bool IsInitialized => OwningThreadId != 0;

    /// <summary>
    /// Gets a value indicating whether the calling thread matches <see cref="OwningThreadId"/>.
    /// </summary>
    public bool IsCurrentThread => OwningThreadId != 0 && OwningThreadId == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Determines whether two <see cref="ThreadAffinity"/> values represent the same thread binding.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when both values have the same <see cref="OwningThreadId"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ThreadAffinity left, ThreadAffinity right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="ThreadAffinity"/> values represent different thread bindings.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ThreadAffinity left, ThreadAffinity right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Captures the current managed thread (<see cref="Environment.CurrentManagedThreadId"/>) as the owner.
    /// </summary>
    /// <returns>An initialized <see cref="ThreadAffinity"/> bound to the calling managed thread.</returns>
    public static ThreadAffinity Capture()
    {
        return new ThreadAffinity(Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if this instance was not initialized or if the calling thread
    /// differs from <see cref="OwningThreadId"/>.
    /// </summary>
    /// <param name="ownerName">Optional component or session name included in the exception message.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="IsInitialized"/> is <see langword="false"/> or when called from a non-owning thread.
    /// </exception>
    public void ThrowIfWrongThread(string? ownerName = null)
    {
        if (OwningThreadId == 0)
        {
            var uninitializedTarget = string.IsNullOrWhiteSpace(ownerName) ? nameof(ThreadAffinity) : ownerName;
            throw new InvalidOperationException($"{uninitializedTarget} has not captured an owning thread.");
        }

        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != OwningThreadId)
        {
            var target = string.IsNullOrWhiteSpace(ownerName) ? "Thread-affinitized instance" : ownerName;
            throw new InvalidOperationException(
                $"{target} is bound to managed thread {OwningThreadId}, but was accessed from managed thread {currentThreadId}.");
        }
    }

    /// <inheritdoc />
    public bool Equals(ThreadAffinity other)
    {
        return OwningThreadId == other.OwningThreadId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ThreadAffinity other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return OwningThreadId;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return OwningThreadId == 0
            ? "ThreadAffinity(Uninitialized)"
            : $"ThreadAffinity(ThreadId={OwningThreadId})";
    }
}
