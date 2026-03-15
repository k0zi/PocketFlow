namespace PocketFlow;

public class BatchNode : Node
{
    public BatchNode(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    internal override object? _Exec(object? items)
    {
        var enumerable = items as System.Collections.IEnumerable;
        if (enumerable == null) return new List<object?>();
        var results = new List<object?>();
        foreach (var item in enumerable)
        {
            results.Add(base._Exec(item));
        }
        return results;
    }
}