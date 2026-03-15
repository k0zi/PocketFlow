namespace PocketFlow;

public class AsyncParallelBatchFlow : AsyncFlow
{
    public AsyncParallelBatchFlow(BaseNode? start = null) : base(start) { }

    internal override async Task<object?> _RunAsync(object shared)
    {
        var pr = await PrepAsync(shared) as IEnumerable<Dictionary<string, object>> ?? Array.Empty<Dictionary<string, object>>();
        var tasks = new List<Task<string?>>();
        foreach (var bp in pr)
        {
            var p = new Dictionary<string, object>(Params);
            foreach (var kvp in bp) p[kvp.Key] = kvp.Value;
            tasks.Add(_OrchAsync(shared, p));
        }
        await Task.WhenAll(tasks);
        return await PostAsync(shared, pr, null);
    }
}