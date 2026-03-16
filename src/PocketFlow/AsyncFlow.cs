namespace PocketFlow;

public class AsyncFlow : AsyncNode
{
    public BaseNode? StartNode { get; set; }

    public AsyncFlow(BaseNode? start = null) : base()
    {
        StartNode = start;
    }

    public BaseNode Start(BaseNode start)
    {
        StartNode = start;
        return start;
    }

    public virtual BaseNode? GetNextNode(BaseNode curr, string? action)
    {
        if (curr.Successors.TryGetValue(action ?? "default", out var next))
            return next;
        if (curr.Successors.Count > 0)
            Console.WriteLine($"Warning: Flow ends: '{action}' not found in {string.Join(", ", curr.Successors.Keys)}");
        return null;
    }

    internal virtual async Task<string?> _OrchAsync(object shared, Dictionary<string, object>? @params = null)
    {
        if (StartNode == null) return null;
        var curr = (BaseNode)StartNode.Clone();
        var p = @params ?? new Dictionary<string, object>(Params);
        string? lastAction = null;
        while (curr != null)
        {
            curr.SetParams(p);
            if (curr is AsyncNode asyncNode)
                lastAction = (await asyncNode._RunAsync(shared))?.ToString();
            else
                lastAction = curr.InternalRun(shared)?.ToString();
            
            var next = GetNextNode(curr, lastAction);
            curr = next != null ? (BaseNode)next.Clone() : null;
        }
        return lastAction;
    }

    internal override async Task<object?> _RunAsync(object shared)
    {
        var p = await PrepAsync(shared);
        var o = await _OrchAsync(shared);
        return await PostAsync(shared, p, o);
    }

    protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes) => Task.FromResult(execRes);
}