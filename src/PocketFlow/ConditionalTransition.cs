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

    public static BaseNode operator >> (ConditionalTransition trans, BaseNode tgt) => trans.Src.Next(tgt, trans.Action);
}