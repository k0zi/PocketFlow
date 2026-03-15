using System.Collections.Generic;
using System.Threading.Tasks;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class AsyncFlowTests
{
    private class AsyncNumberNode : AsyncNode
    {
        private readonly int _number;
        public AsyncNumberNode(int number) : base() => _number = number;
        protected override Task<object?> PrepAsync(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["current"] = _number;
            return Task.FromResult<object?>(null);
        }
    }

    private class AsyncAddNode : AsyncNode
    {
        private readonly int _number;
        public AsyncAddNode(int number) : base() => _number = number;
        protected override Task<object?> PrepAsync(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["current"] += _number;
            return Task.FromResult<object?>(null);
        }
    }

    [Fact]
    public async Task TestAsyncFlowBasic()
    {
        var shared = new Dictionary<string, int> { ["current"] = 0 };
        var flow = new AsyncFlow();
        _ = flow.Start(new AsyncNumberNode(5)) >> new AsyncAddNode(10);
        await flow.RunAsync(shared);
        Assert.Equal(15, shared["current"]);
    }
}
