namespace PocketFlow;

public class AsyncBatchNode : AsyncNode
{
    public AsyncBatchNode(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    internal override async Task<object?> _ExecAsync(object? items)
    {
        var enumerable = items as System.Collections.IEnumerable;
        if (enumerable == null) return new List<object?>();
        var results = new List<object?>();
        foreach (var item in enumerable)
        {
            results.Add(await _ExecSingleAsync(item));
        }
        return results;
    }
}