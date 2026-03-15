using System.Collections.Generic;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class FlowBasicTests
{
    private class NumberNode : Node
    {
        private readonly int _number;
        public NumberNode(int number) : base() => _number = number;
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["current"] = _number;
            return null;
        }
    }

    private class AddNode : Node
    {
        private readonly int _number;
        public AddNode(int number) : base() => _number = number;
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["current"] += _number;
            return null;
        }
    }

    private class MultiplyNode : Node
    {
        private readonly int _number;
        public MultiplyNode(int number) : base() => _number = number;
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            sharedStorage["current"] *= _number;
            return null;
        }
    }

    private class CheckPositiveNode : Node
    {
        protected override object? Post(object shared, object? prepRes, object? execRes)
        {
            var sharedStorage = (Dictionary<string, int>)shared;
            return sharedStorage["current"] >= 0 ? "positive" : "negative";
        }
    }

    private class NoOpNode : Node { }

    private class EndSignalNode : Node
    {
        private readonly string _signal;
        public EndSignalNode(string signal = "finished") : base() => _signal = signal;
        protected override object? Post(object shared, object? prepRes, object? execRes) => _signal;
    }

    [Fact]
    public void TestStartMethodInitialization()
    {
        var sharedStorage = new Dictionary<string, int>();
        var n1 = new NumberNode(5);
        var pipeline = new Flow();
        pipeline.Start(n1);
        var lastAction = pipeline.Run(sharedStorage);
        Assert.Equal(5, sharedStorage["current"]);
        Assert.Null(lastAction);
    }

    [Fact]
    public void TestStartMethodChaining()
    {
        var sharedStorage = new Dictionary<string, int>();
        var pipeline = new Flow();
        pipeline.Start(new NumberNode(5)).Next(new AddNode(3)).Next(new MultiplyNode(2));
        var lastAction = pipeline.Run(sharedStorage);
        Assert.Equal(16, sharedStorage["current"]);
        Assert.Null(lastAction);
    }

    [Fact]
    public void TestSequenceWithRShift()
    {
        var sharedStorage = new Dictionary<string, int>();
        var n1 = new NumberNode(5);
        var n2 = new AddNode(3);
        var n3 = new MultiplyNode(2);
        var pipeline = new Flow();
        _ = pipeline.Start(n1) >> n2 >> n3;
        var lastAction = pipeline.Run(sharedStorage);
        Assert.Equal(16, sharedStorage["current"]);
        Assert.Null(lastAction);
    }

    [Fact]
    public void TestBranchingPositive()
    {
        var sharedStorage = new Dictionary<string, int>();
        var startNode = new NumberNode(5);
        var checkNode = new CheckPositiveNode();
        var addIfPositive = new AddNode(10);
        var addIfNegative = new AddNode(-20);
        var pipeline = new Flow();
        _ = pipeline.Start(startNode) >> checkNode;
        _ = checkNode - "positive" >> addIfPositive;
        _ = checkNode - "negative" >> addIfNegative;
        var lastAction = pipeline.Run(sharedStorage);
        Assert.Equal(15, sharedStorage["current"]);
        Assert.Null(lastAction);
    }

    [Fact]
    public void TestBranchingNegative()
    {
        var sharedStorage = new Dictionary<string, int>();
        var startNode = new NumberNode(-5);
        var checkNode = new CheckPositiveNode();
        var addIfPositive = new AddNode(10);
        var addIfNegative = new AddNode(-20);
        var pipeline = new Flow();
        _ = pipeline.Start(startNode) >> checkNode;
        _ = checkNode - "positive" >> addIfPositive;
        _ = checkNode - "negative" >> addIfNegative;
        var lastAction = pipeline.Run(sharedStorage);
        Assert.Equal(-25, sharedStorage["current"]);
        Assert.Null(lastAction);
    }

    [Fact]
    public void TestCycleUntilNegativeEndsWithSignal()
    {
        var sharedStorage = new Dictionary<string, int>();
        var n1 = new NumberNode(10);
        var check = new CheckPositiveNode();
        var subtract3 = new AddNode(-3);
        var endNode = new EndSignalNode("cycle_done");
        var pipeline = new Flow();
        _ = pipeline.Start(n1) >> check;
        _ = check - "positive" >> subtract3;
        _ = check - "negative" >> endNode;
        _ = subtract3 >> check;
        var lastAction = pipeline.Run(sharedStorage);
        Assert.Equal(-2, sharedStorage["current"]);
        Assert.Equal("cycle_done", lastAction);
    }
}
