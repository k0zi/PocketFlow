using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PocketFlow;

public class Flow : BaseNode
{
    public BaseNode? StartNode { get; set; }

    public Flow(BaseNode? start = null)
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

    internal virtual string? _Orch(object shared, Dictionary<string, object>? @params = null)
    {
        if (StartNode == null) return null;
        var curr = (BaseNode)StartNode.Clone();
        var p = @params ?? new Dictionary<string, object>(Params);
        string? lastAction = null;
        while (curr != null)
        {
            curr.SetParams(p);
            lastAction = curr.InternalRun(shared)?.ToString();
            var next = GetNextNode(curr, lastAction);
            curr = next != null ? (BaseNode)next.Clone() : null;
        }
        return lastAction;
    }

    internal override object? InternalRun(object shared)
    {
        var p = Prepare(shared);
        var o = _Orch(shared);
        return Post(shared, p, o);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes) => execRes;
}