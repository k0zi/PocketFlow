namespace PocketFlow;

public class BaseNode : ICloneable
{
    public Dictionary<string, object> Params { get; set; } = new();
    public Dictionary<string, BaseNode> Successors { get; set; } = new();

    public virtual void SetParams(Dictionary<string, object> @params) => Params = @params;

    public virtual BaseNode Next(BaseNode node, string action = "default")
    {
        if (Successors.ContainsKey(action))
            Console.WriteLine($"Warning: Overwriting successor for action '{action}'");
        Successors[action] = node;
        return node;
    }

    protected virtual object? Prep(object shared) => null;
    protected virtual object? Exec(object? prepRes) => null;
    protected virtual object? Post(object shared, object? prepRes, object? execRes) => execRes;

    internal virtual object? _Exec(object? prepRes) => Exec(prepRes);

    internal virtual object? _Run(object shared)
    {
        var p = Prep(shared);
        var e = _Exec(p);
        return Post(shared, p, e);
    }

    public virtual object? Run(object shared)
    {
        if (Successors.Count > 0)
            Console.WriteLine("Warning: Node won't run successors. Use Flow.");
        return _Run(shared);
    }

    public static BaseNode operator >> (BaseNode src, BaseNode tgt) => src.Next(tgt);
    public static ConditionalTransition operator - (BaseNode src, string action) => new(src, action);

    public virtual object Clone() => MemberwiseClone();
}