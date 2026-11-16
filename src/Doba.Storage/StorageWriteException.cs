namespace Doba.Storage;

/// <summary>
/// Thrown by <see cref="Doba.Core.Store.ITaskPersistence.FlushAsync"/> (and <see cref="DobaDatabase.DisposeAsync"/>) when one
/// or more queued writes failed since the previous flush. <see cref="Exception.InnerException"/> is the last failure.
/// The queue keeps working after a failure; later writes are attempted normally.
/// </summary>
public sealed class StorageWriteException : Exception
{
    public StorageWriteException()
        : base("A queued write failed to reach the database.")
    {
    }

    public StorageWriteException(string message)
        : base(message)
    {
    }

    public StorageWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal StorageWriteException(int failedWrites, Exception lastError)
        : base($"{failedWrites} queued write(s) failed to reach the database. Last error: {lastError.Message}", lastError)
    {
        FailedWrites = failedWrites;
    }

    /// <summary>Number of queued operations that failed since the previous flush.</summary>
    public int FailedWrites { get; }
}

/// <summary>Raised on the background writer thread for every failed write.</summary>
public sealed class StorageWriteFailedEventArgs(Exception error) : EventArgs
{
    public Exception Error { get; } = error;
}
