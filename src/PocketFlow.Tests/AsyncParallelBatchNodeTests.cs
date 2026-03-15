using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using PocketFlow;
using Xunit;

namespace PocketFlow.Tests;

public class AsyncParallelBatchNodeTests
{
    private class AsyncParallelNumberProcessor : AsyncParallelBatchNode
    {
        private readonly int _delay;
        public AsyncParallelNumberProcessor(int delay = 100) : base() => _delay = delay;

        protected override Task<object?> PrepAsync(object shared)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            var numbers = (List<int>)sharedStorage.GetValueOrDefault("input_numbers", new List<int>());
            return Task.FromResult<object?>(numbers);
        }

        protected override async Task<object?> ExecAsync(object? number)
        {
            await Task.Delay(_delay);
            return (int)number! * 2;
        }

        protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
        {
            var sharedStorage = (Dictionary<string, object>)shared;
            sharedStorage["processed_numbers"] = execRes!;
            return Task.FromResult<object?>("processed");
        }
    }

    [Fact]
    public async Task TestParallelProcessing()
    {
        var sharedStorage = new Dictionary<string, object>
        {
            ["input_numbers"] = Enumerable.Range(0, 5).ToList()
        };

        var processor = new AsyncParallelNumberProcessor(delay: 100);
        var sw = Stopwatch.StartNew();
        await processor.RunAsync(sharedStorage);
        sw.Stop();

        var expected = new List<object> { 0, 2, 4, 6, 8 };
        Assert.Equal(expected, (List<object?>)sharedStorage["processed_numbers"]);
        Assert.True(sw.ElapsedMilliseconds < 300, $"Duration was {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task TestEmptyInput()
    {
        var sharedStorage = new Dictionary<string, object>
        {
            ["input_numbers"] = new List<int>()
        };

        var processor = new AsyncParallelNumberProcessor();
        await processor.RunAsync(sharedStorage);

        Assert.Empty((List<object?>)sharedStorage["processed_numbers"]);
    }

    [Fact]
    public async Task TestConcurrentExecution()
    {
        var executionOrder = new List<int>();

        var sharedStorage = new Dictionary<string, object>
        {
            ["input_numbers"] = new List<int> { 0, 1, 2, 3 }
        };

        var processor = new OrderTrackingProcessor(executionOrder);
        await processor.RunAsync(sharedStorage);

        // Odd numbers (1, 3) have 50ms delay, even numbers (0, 2) have 100ms delay.
        // So 1 and 3 should appear before 0 and 2.
        
        var indexOf0 = executionOrder.IndexOf(0);
        var indexOf1 = executionOrder.IndexOf(1);
        var indexOf2 = executionOrder.IndexOf(2);
        var indexOf3 = executionOrder.IndexOf(3);

        Assert.True(indexOf1 < indexOf0);
        Assert.True(indexOf1 < indexOf2);
        Assert.True(indexOf3 < indexOf0);
        Assert.True(indexOf3 < indexOf2);
    }

    private class OrderTrackingProcessor : AsyncParallelNumberProcessor
    {
        private readonly List<int> _order;
        public OrderTrackingProcessor(List<int> order) : base() => _order = order;

        protected override async Task<object?> ExecAsync(object? item)
        {
            var i = (int)item!;
            var delay = (i % 2 == 0) ? 100 : 50;
            await Task.Delay(delay);
            lock (_order)
            {
                _order.Add(i);
            }
            return i;
        }
    }
}
