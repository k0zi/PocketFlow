using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class FallbackTests
{
    private class FallbackNode : Node
    {
        private readonly bool _shouldFail;
        public FallbackNode(bool shouldFail = true, int maxRetries = 1) : base(maxRetries) => _shouldFail = shouldFail;

        protected override object? Prepare(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            var result = new Dictionary<string, object> { ["attempts"] = 0 };
            if (!sharedStorage.ContainsKey("results")) sharedStorage["results"] = new List<Dictionary<string, object>>();
            ((List<Dictionary<string, object>>)sharedStorage["results"]).Add(result);
            return result;
        }

        protected override object? Execute(object? prepRes)
        {
            var res = (Dictionary<string, object>)prepRes!;
            res["attempts"] = (int)res["attempts"] + 1;
            if (_shouldFail) throw new Exception("Intentional failure");
            return "success";
        }

        protected override object? ExecFallback(object? prepRes, Exception exc)
        {
            var res = (Dictionary<string, object>)prepRes!;
            res["result"] = "fallback";
            return "fallback";
        }
    }

    private class AsyncFallbackNode : AsyncNode
    {
        private readonly bool _shouldFail;
        public AsyncFallbackNode(bool shouldFail = true, int maxRetries = 1) : base(maxRetries) => _shouldFail = shouldFail;

        protected override Task<object?> PrepAsync(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            var result = new Dictionary<string, object> { ["attempts"] = 0 };
            if (!sharedStorage.ContainsKey("results")) sharedStorage["results"] = new List<Dictionary<string, object>>();
            ((List<Dictionary<string, object>>)sharedStorage["results"]).Add(result);
            return Task.FromResult<object?>(result);
        }

        protected override Task<object?> ExecAsync(object? prepRes)
        {
            var res = (Dictionary<string, object>)prepRes!;
            res["attempts"] = (int)res["attempts"] + 1;
            if (_shouldFail) throw new Exception("Intentional failure");
            return Task.FromResult<object?>("async_success");
        }

        protected override Task<object?> ExecFallbackAsync(object? prepRes, Exception exc)
        {
            var res = (Dictionary<string, object>)prepRes!;
            res["result"] = "async_fallback";
            return Task.FromResult<object?>("async_fallback");
        }
    }

    [Fact]
    public void TestFallbackSuccessAfterRetries()
    {
        var sharedStorage = new Dictionary<string, object>();
        // We want it to fail twice then succeed. 
        // Our current Node implementation doesn't easily support changing behavior per retry from outside.
        // But we can implement it in Exec.
        
        var node = new DynamicFallbackNode(failUntil: 2, maxRetries: 3);
        var result = node.Run(sharedStorage);
        
        var results = (List<Dictionary<string, object>>)sharedStorage["results"];
        Assert.Equal(1, results.Count);
        Assert.Equal(2, results[0]["attempts"]); // Wait, if it fails until 2, it should succeed on 2nd attempt? 
        // Let's check Python's logic.
        // If fail_until=2:
        // Attempt 1: fails
        // Attempt 2: succeeds
        // So attempts should be 2.
        Assert.Equal("success", result);
    }

    private class DynamicFallbackNode : FallbackNode
    {
        private readonly int _failUntil;
        public DynamicFallbackNode(int failUntil, int maxRetries) : base(true, maxRetries) => _failUntil = failUntil;
        protected override object? Execute(object? prepRes)
        {
            var res = (Dictionary<string, object>)prepRes!;
            res["attempts"] = (int)res["attempts"] + 1;
            if ((int)res["attempts"] < _failUntil) throw new Exception("Intentional failure");
            return "success";
        }
    }

    [Fact]
    public void TestFallbackTriggered()
    {
        var sharedStorage = new Dictionary<string, object>();
        var node = new FallbackNode(shouldFail: true, maxRetries: 3);
        var result = node.Run(sharedStorage);
        
        var results = (List<Dictionary<string, object>>)sharedStorage["results"];
        Assert.Equal(1, results.Count);
        Assert.Equal(3, results[0]["attempts"]);
        Assert.Equal("fallback", results[0]["result"]);
        Assert.Equal("fallback", result);
    }

    [Fact]
    public async Task TestAsyncFallbackTriggered()
    {
        var sharedStorage = new Dictionary<string, object>();
        var node = new AsyncFallbackNode(shouldFail: true, maxRetries: 3);
        var result = await node.RunAsync(sharedStorage);
        
        var results = (List<Dictionary<string, object>>)sharedStorage["results"];
        Assert.Equal(1, results.Count);
        Assert.Equal(3, results[0]["attempts"]);
        Assert.Equal("async_fallback", results[0]["result"]);
        Assert.Equal("async_fallback", result);
    }
}
