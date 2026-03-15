namespace PocketFlow;

public class BatchFlow : Flow
{
    public BatchFlow(BaseNode? start = null) : base(start) { }

    internal override object? _Run(object shared)
    {
        var pr = Prep(shared) as IEnumerable<Dictionary<string, object>> ?? Array.Empty<Dictionary<string, object>>();
        foreach (var bp in pr)
        {
            var p = new Dictionary<string, object>(Params);
            foreach (var kvp in bp) p[kvp.Key] = kvp.Value;
            _Orch(shared, p);
        }
        return Post(shared, pr, null);
    }
}