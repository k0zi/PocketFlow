namespace PocketFlow;

public class AsyncParallelBatchNode : AsyncBatchNode
{
    public AsyncParallelBatchNode(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    internal override async Task<object?> _ExecAsync(object? items)
    {
        var enumerable = items as System.Collections.IEnumerable;
        if (enumerable == null) return new List<object?>();
        
        var tasks = new List<Task<object?>>();
        foreach (var item in enumerable)
        {
            tasks.Add(_ExecSingleAsync(item));
        }
        return (await Task.WhenAll(tasks)).ToList();
    }
}