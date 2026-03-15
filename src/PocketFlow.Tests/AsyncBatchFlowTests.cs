using System.Collections.Generic;
using System.Threading.Tasks;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class AsyncBatchFlowTests
{
    private class AsyncAddNode : AsyncNode
    {
        protected override Task<object?> PrepAsync(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["sum"] += (int)Params["value"];
            return Task.FromResult<object?>(null);
        }
    }

    private class MyAsyncBatchFlow : AsyncBatchFlow
    {
        protected override Task<object?> PrepAsync(object shared)
        {
            return Task.FromResult<object?>(new List<Dictionary<string, object>>
            {
                new() { ["value"] = 1 },
                new() { ["value"] = 2 },
                new() { ["value"] = 3 }
            });
        }
    }

    [Fact]
    public async Task TestAsyncBatchFlow()
    {
        var shared = new Dictionary<string, int> { ["sum"] = 0 };
        var flow = new MyAsyncBatchFlow();
        flow.Start(new AsyncAddNode());
        await flow.RunAsync(shared);
        Assert.Equal(6, shared["sum"]);
    }
}
