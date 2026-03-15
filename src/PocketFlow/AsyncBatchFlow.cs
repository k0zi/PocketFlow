namespace PocketFlow;

public class AsyncBatchFlow : AsyncFlow
{
    public AsyncBatchFlow(BaseNode? start = null) : base(start) { }

    internal override async Task<object?> _RunAsync(object shared)
    {
        var pr = await PrepAsync(shared) as IEnumerable<Dictionary<string, object>> ?? Array.Empty<Dictionary<string, object>>();
        foreach (var bp in pr)
        {
            var p = new Dictionary<string, object>(Params);
            foreach (var kvp in bp) p[kvp.Key] = kvp.Value;
            await _OrchAsync(shared, p);
        }
        return await PostAsync(shared, pr, null);
    }
}