using System.Collections.Generic;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class BatchFlowTests
{
    private class AddNode : Node
    {
        protected override object? Prepare(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["sum"] += (int)Params["value"];
            return null;
        }
    }

    private class MyBatchFlow : BatchFlow
    {
        protected override object? Prepare(object shared)
        {
            return new List<Dictionary<string, object>>
            {
                new() { ["value"] = 1 },
                new() { ["value"] = 2 },
                new() { ["value"] = 3 }
            };
        }
    }

    [Fact]
    public void TestBatchFlow()
    {
        var shared = new Dictionary<string, int> { ["sum"] = 0 };
        var flow = new MyBatchFlow();
        flow.Start(new AddNode());
        flow.Run(shared);
        Assert.Equal(6, shared["sum"]);
    }
}
