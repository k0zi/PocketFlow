namespace PocketFlow;

public class AsyncNode : Node
{
    public AsyncNode(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    protected virtual Task<object?> PrepAsync(object shared) => Task.FromResult<object?>(null);
    protected virtual Task<object?> ExecAsync(object? prepRes) => Task.FromResult<object?>(null);
    protected virtual Task<object?> ExecFallbackAsync(object? prepRes, Exception exc) => throw exc;
    protected virtual Task<object?> PostAsync(object shared, object? prepRes, object? execRes) => Task.FromResult<object?>(execRes);

    protected async Task<object?> _ExecSingleAsync(object? prepRes)
    {
        for (CurRetry = 0; CurRetry < MaxRetries; CurRetry++)
        {
            try
            {
                return await ExecAsync(prepRes);
            }
            catch (Exception e)
            {
                if (CurRetry == MaxRetries - 1)
                    return await ExecFallbackAsync(prepRes, e);
                if (Wait > 0)
                    await Task.Delay(Wait * 1000);
            }
        }
        return null;
    }

    internal virtual Task<object?> _ExecAsync(object? prepRes) => _ExecSingleAsync(prepRes);

    public virtual async Task<object?> RunAsync(object shared)
    {
        if (Successors.Count > 0)
            Console.WriteLine("Warning: Node won't run successors. Use AsyncFlow.");
        return await _RunAsync(shared);
    }

    internal virtual async Task<object?> _RunAsync(object shared)
    {
        var p = await PrepAsync(shared);
        var e = await _ExecAsync(p);
        return await PostAsync(shared, p, e);
    }

    internal override object? InternalRun(object shared) => throw new InvalidOperationException("Use RunAsync.");
}