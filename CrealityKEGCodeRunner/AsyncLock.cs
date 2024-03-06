namespace CrealityKEGCodeRunner;

internal class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IDisposable Lock()
    {
        _lock.Wait();

        return new Finalizer(this);
    }

    public IDisposable Lock(TimeSpan timeout)
    {
        if (!_lock.Wait(timeout))
            throw new TimeoutException("Failed to acquire lock");

        return new Finalizer(this);
    }

    public async Task<IDisposable> LockAsync()
    {
        await _lock.WaitAsync();

        return new Finalizer(this);
    }

    public async Task<IDisposable> LockAsync(TimeSpan timeout)
    {
        if (!await _lock.WaitAsync(timeout))
            throw new TimeoutException("Failed to acquire lock");

        return new Finalizer(this);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private class Finalizer(AsyncLock owner) : IDisposable
    {
        public void Dispose()
        {
            owner._lock.Release();
        }
    }
}
