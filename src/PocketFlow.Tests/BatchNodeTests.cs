using System.Collections.Generic;
using System.Linq;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class BatchNodeTests
{
    private class ArrayChunkNode : BatchNode
    {
        private readonly int _chunkSize;
        public ArrayChunkNode(int chunkSize = 10) : base() => _chunkSize = chunkSize;

        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            var array = (List<int>)sharedStorage.GetValueOrDefault("input_array", new List<int>());
            var chunks = new List<List<int>>();
            for (int start = 0; start < array.Count; start += _chunkSize)
            {
                int end = Math.Min(start + _chunkSize, array.Count);
                chunks.Add(array.GetRange(start, end - start));
            }
            return chunks;
        }

        protected override object? Exec(object? chunk)
        {
            var list = (List<int>)chunk!;
            return list.Sum();
        }

        protected override object? Post(object shared, object? prepRes, object? execRes)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["chunk_results"] = execRes!;
            return "default";
        }
    }

    private class SumReduceNode : Node
    {
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            var chunkResults = (List<object?>)sharedStorage.GetValueOrDefault("chunk_results", new List<object?>());
            var total = chunkResults.Sum(x => (int)x!);
            sharedStorage["total"] = total;
            return null;
        }
    }

    [Fact]
    public void TestArrayChunking()
    {
        var sharedStorage = new Dictionary<string, object>
        {
            ["input_array"] = Enumerable.Range(0, 25).ToList()
        };

        var chunkNode = new ArrayChunkNode(chunkSize: 10);
        chunkNode.Run(sharedStorage);
        var results = (List<object?>)sharedStorage["chunk_results"];
        Assert.Equal(new List<object> { 45, 145, 110 }, results);
    }

    [Fact]
    public void TestMapReduceSum()
    {
        var array = Enumerable.Range(0, 100).ToList();
        var expectedSum = array.Sum();

        var sharedStorage = new Dictionary<string, object>
        {
            ["input_array"] = array
        };

        var chunkNode = new ArrayChunkNode(chunkSize: 10);
        var reduceNode = new SumReduceNode();

        _ = chunkNode >> reduceNode;

        var pipeline = new Flow(start: chunkNode);
        pipeline.Run(sharedStorage);

        Assert.Equal(expectedSum, sharedStorage["total"]);
    }
}
