using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class AsyncBatchNodeTests
{
    private class AsyncArrayChunkNode : AsyncBatchNode
    {
        private readonly int _chunkSize;
        public AsyncArrayChunkNode(int chunkSize = 10) : base() => _chunkSize = chunkSize;

        protected override Task<object?> PrepAsync(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            var array = (List<int>)sharedStorage.GetValueOrDefault("input_array", new List<int>());
            var chunks = new List<List<int>>();
            for (int start = 0; start < array.Count; start += _chunkSize)
            {
                int end = Math.Min(start + _chunkSize, array.Count);
                chunks.Add(array.GetRange(start, end - start));
            }
            return Task.FromResult<object?>(chunks);
        }

        protected override async Task<object?> ExecAsync(object? chunk)
        {
            await Task.Delay(10);
            var list = (List<int>)chunk!;
            return list.Sum();
        }

        protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["chunk_results"] = execRes!;
            return Task.FromResult<object?>("processed");
        }
    }

    [Fact]
    public async Task TestArrayChunking()
    {
        var sharedStorage = new Dictionary<string, object>
        {
            ["input_array"] = Enumerable.Range(0, 25).ToList()
        };

        var chunkNode = new AsyncArrayChunkNode(chunkSize: 10);
        await chunkNode.RunAsync(sharedStorage);

        var results = (List<object?>)sharedStorage["chunk_results"];
        Assert.Equal(new List<object> { 45, 145, 110 }, results);
    }
}
