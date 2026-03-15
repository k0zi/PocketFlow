namespace PocketFlow;

public class Node : BaseNode
{
    public int MaxRetries { get; set; }
    public int Wait { get; set; }
    public int CurRetry { get; protected set; }

    public Node(int maxRetries = 1, int wait = 0)
    {
        MaxRetries = maxRetries;
        Wait = wait;
    }

    protected virtual object? ExecFallback(object? prepRes, Exception exc) => throw exc;

    internal override object? _Exec(object? prepRes)
    {
        for (CurRetry = 0; CurRetry < MaxRetries; CurRetry++)
        {
            try
            {
                return Exec(prepRes);
            }
            catch (Exception e)
            {
                if (CurRetry == MaxRetries - 1)
                    return ExecFallback(prepRes, e);
                if (Wait > 0)
                    System.Threading.Thread.Sleep(Wait * 1000);
            }
        }
        return null;
    }
}