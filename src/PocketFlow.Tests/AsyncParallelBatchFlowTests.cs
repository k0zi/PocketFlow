using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class AsyncParallelBatchFlowTests
{
    private class AsyncSleepNode : AsyncNode
    {
        protected override async Task<object?> PrepAsync(object shared)
        {
            await Task.Delay(100);
            var sharedStorage = (Dictionary<string, object>)shared;
            var results = (Dictionary<string, List<int>>)sharedStorage["results"];
            var group = (string)Params["group"];
            var value = (int)Params["value"];
            
            lock (results)
            {
                if (!results.ContainsKey(group)) results[group] = new List<int>();
                results[group].Add(value);
            }
            return null;
        }
    }

    private class MyAsyncParallelBatchFlow : AsyncParallelBatchFlow
    {
        protected override Task<object?> PrepAsync(object shared)
        {
            return Task.FromResult<object?>(new List<Dictionary<string, object>>
            {
                new() { ["group"] = "A", ["value"] = 2 },
                new() { ["group"] = "A", ["value"] = 4 },
                new() { ["group"] = "B", ["value"] = 6 },
                new() { ["group"] = "B", ["value"] = 8 }
            });
        }
    }

    [Fact]
    public async Task TestParallelBatchFlow()
    {
        var shared = new Dictionary<string, object>
        {
            ["results"] = new Dictionary<string, List<int>>()
        };
        var flow = new MyAsyncParallelBatchFlow();
        flow.Start(new AsyncSleepNode());
        
        var sw = Stopwatch.StartNew();
        await flow.RunAsync(shared);
        sw.Stop();
        
        // Parallel execution: 4 items, each 100ms. Should take ~100ms total.
        Assert.True(sw.ElapsedMilliseconds < 300, $"Duration was {sw.ElapsedMilliseconds}ms");
        
        var results = (Dictionary<string, List<int>>)shared["results"];
        Assert.Equal(2, results.Count);
        
        Assert.Contains("A", results.Keys);
        Assert.Contains("B", results.Keys);
        
        Assert.Equal(new List<int> { 2, 4 }, results["A"].OrderBy(x => x).ToList());
        Assert.Equal(new List<int> { 6, 8 }, results["B"].OrderBy(x => x).ToList());
    }
}
