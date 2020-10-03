using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using FluentAssertions;
using Xunit;

namespace Voxel.MiddyNet.Tests
{
    public class MiddyNetShould
    {
        private readonly List<string> logLines = new List<string>();
        private readonly List<string> contextLines = new List<string>();
        private const string FunctionLog = "FunctionCode";
        private const string MiddlewareBeforeLog = "MiddlewareBeforeCode";
        private const string MiddlewareAfterLog = "MiddlewareAfterCode";
        private const string ContextLog = "ContextLog";
        private const string ContextKeyLog = "ContextKeyLog";
        private List<Exception> middlewareExceptions = new List<Exception>();

        public class TestLambdaFunction : MiddyNet<int, int>
        {
            public TestLambdaFunction(List<string> logLines, List<string> contextLogLines, int numberOfMiddlewares, bool withFailingMiddleware = false, List<Exception> exceptions = null)
            {
                LogLines = logLines;
                ContextLogLines = contextLogLines;
                Exceptions = exceptions ?? new List<Exception>();
                for (var i = 0; i < numberOfMiddlewares; i++)
                {
                    Use(new TestMiddleware(logLines, i+1, withFailingMiddleware));
                }
            }

            public List<string> LogLines { get; set; }
            public List<string> ContextLogLines { get; }
            public List<Exception> Exceptions { get; set; }

            protected override Task<int> Handle(int lambdaEvent, MiddyNetContext context)
            {
                LogLines.Add(FunctionLog);
                ContextLogLines.AddRange(context.AdditionalContext.Select(kv => $"{kv.Key}-{kv.Value}"));
                Exceptions.AddRange(context.MiddlewareExceptions);

                return Task.FromResult(0);
            }
        }

        public class MiddlewareException : Exception { }

        public class TestMiddleware : ILambdaMiddleware<int, int>
        {
            private readonly int position;
            public List<string> LogLines { get; }
            public bool Failing { get; }

            public TestMiddleware(List<string> logLines, int position, bool failing)
            {
                this.position = position;
                LogLines = logLines;
                Failing = failing;
            }

            public Task Before(int lambdaEvent, MiddyNetContext context)
            {
                LogLines.Add($"{MiddlewareBeforeLog}-{position}");
                context.AdditionalContext.Add($"{ContextKeyLog}-{position}", $"{ContextLog}-{position}");

                if(Failing) throw new MiddlewareException();

                return Task.CompletedTask;
            }

            public Task<int> After(int lambdaResponse, MiddyNetContext context)
            {
                LogLines.Add($"{MiddlewareAfterLog}-{position}");

                if (Failing) throw new MiddlewareException();
                return Task.FromResult(0);
            }
        }

        [Fact]
        public async Task RunMiddlewareAroundTheFunction()
        {
            var lambdaFunction = new TestLambdaFunction(logLines, contextLines, 1);

            await lambdaFunction.Handler(1, new FakeLambdaContext());

            logLines.Should().ContainInOrder($"{MiddlewareBeforeLog}-1", FunctionLog, $"{MiddlewareAfterLog}-1");
        }

        [Fact]
        public async Task RunMiddlewareBeforeActionInOrder()
        {
            var lambdaFunction = new TestLambdaFunction(logLines, contextLines, 2);

            await lambdaFunction.Handler(1, new FakeLambdaContext());

            logLines.Should().ContainInOrder($"{MiddlewareBeforeLog}-1", $"{MiddlewareBeforeLog}-2", FunctionLog);
        }

        [Fact]
        public async Task RunMiddlewareAfterActionInInverseOrder()
        {
            var lambdaFunction = new TestLambdaFunction(logLines, contextLines, 2);

            await lambdaFunction.Handler(1, new FakeLambdaContext());

            logLines.Should().ContainInOrder(FunctionLog, $"{MiddlewareAfterLog}-2", $"{MiddlewareAfterLog}-1");
        }

        [Fact]
        public async Task CleanTheContextBetweenCalls()
        {
            var lambdaFunction = new TestLambdaFunction(logLines, contextLines, 2);

            await lambdaFunction.Handler(1, new FakeLambdaContext());
            contextLines.Should().HaveCount(2);
            await lambdaFunction.Handler(1, new FakeLambdaContext());
            contextLines.Should().HaveCount(4);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void NotifyErrorOnBefore(int numberOfMiddlewares)
        {
            var lambdaFunction = new TestLambdaFunction(logLines, contextLines, numberOfMiddlewares, true, middlewareExceptions);

            Func<Task> act = async () => await lambdaFunction.Handler(1, new FakeLambdaContext());
            act.Should().Throw<AggregateException>();

            middlewareExceptions.Should().HaveCount(numberOfMiddlewares);
            middlewareExceptions.Should().AllBeOfType<MiddlewareException>();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void NotifyErrorOnAfter(int numberOfMiddlewares)
        {
            var lambdaFunction = new TestLambdaFunction(logLines, contextLines, numberOfMiddlewares, true, middlewareExceptions);
            
            Func<Task> act = async () => await lambdaFunction.Handler(1, new FakeLambdaContext());

            act.Should().Throw<AggregateException>().Where(a =>
                a.InnerExceptions.Count == numberOfMiddlewares && a.InnerExceptions.All(e => e is MiddlewareException));
        }
    }

    public class FakeLambdaContext : ILambdaContext
    {
        public string AwsRequestId { get; }
        public IClientContext ClientContext { get; }
        public string FunctionName { get; }
        public string FunctionVersion { get; }
        public ICognitoIdentity Identity { get; }
        public string InvokedFunctionArn { get; }
        public ILambdaLogger Logger { get; }
        public string LogGroupName { get; }
        public string LogStreamName { get; }
        public int MemoryLimitInMB { get; }
        public TimeSpan RemainingTime { get; }
    }
}
