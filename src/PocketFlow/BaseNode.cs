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

    protected virtual object? Prepare(object shared) => null;
    protected virtual object? Execute(object? prepRes) => null;
    protected virtual object? Post(object shared, object? prepRes, object? execRes) => execRes;

    internal virtual object? InternalExecute(object? prepRes) => Execute(prepRes);

    internal virtual object? InternalRun(object shared)
    {
        var p = Prepare(shared);
        var e = InternalExecute(p);
        return Post(shared, p, e);
    }

    public virtual object? Run(object shared)
    {
        if (Successors.Count > 0)
            Console.WriteLine("Warning: Node won't run successors. Use Flow.");
        return InternalRun(shared);
    }
    
    public BaseNode Then(BaseNode node) 
        => Next(node);

    public ConditionalTransition On(string action) 
        => new(this, action);

    public virtual object Clone() 
        => MemberwiseClone();
}