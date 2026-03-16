namespace PocketFlow;

public class ConditionalTransition
{
    public BaseNode Src { get; }
    public string Action { get; }

    public ConditionalTransition(BaseNode src, string action)
    {
        Src = src;
        Action = action;
    }

    public BaseNode Then(BaseNode tgt) => Src.Next(tgt, Action);
}