using System.Collections.Generic;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class FlowCompositionTests
{
    private class AddNode : Node
    {
        private readonly int _value;
        public AddNode(int value) : base() => _value = value;
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["current"] = (int)sharedStorage["current"] + _value;
            return null;
        }
    }

    private class MultiplyNode : Node
    {
        private readonly int _value;
        public MultiplyNode(int value) : base() => _value = value;
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["current"] = (int)sharedStorage["current"] * _value;
            return null;
        }
    }

    private class SignalNode : Node
    {
        private readonly string _signal;
        public SignalNode(string signal) : base() => _signal = signal;
        protected override object? Post(object shared, object? prepRes, object? execRes)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["last_signal_emitted"] = _signal;
            return _signal;
        }
    }

    private class PathNode : Node
    {
        private readonly string _path;
        public PathNode(string path) : base() => _path = path;
        protected override object? Prep(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["path_taken"] = _path;
            return null;
        }
    }

    [Fact]
    public void TestNestedFlow()
    {
        var sharedStorage = new Dictionary<string, object> { ["current"] = 10 };
        
        var innerFlow = new Flow();
        _ = innerFlow.Start(new AddNode(5)) >> new MultiplyNode(2) >> new SignalNode("inner_done");
        
        var outerFlow = new Flow();
        var pathANode = new PathNode("A");
        var pathBNode = new PathNode("B");
        
        outerFlow.Start(innerFlow);
        _ = innerFlow - "inner_done" >> pathBNode;
        _ = innerFlow - "default" >> pathANode;
        
        var lastActionOuter = outerFlow.Run(sharedStorage);
        
        Assert.Equal(30, sharedStorage["current"]); // (10 + 5) * 2
        Assert.Equal("inner_done", sharedStorage["last_signal_emitted"]);
        Assert.Equal("B", sharedStorage["path_taken"]);
        Assert.Null(lastActionOuter);
    }
}
