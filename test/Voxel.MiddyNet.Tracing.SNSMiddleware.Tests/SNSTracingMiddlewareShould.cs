using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Voxel.MiddyNet.Tracing.SNSMiddleware.Tests
{
    public class SNSTracingMiddlewareShould
    {
        [Fact]
        public async Task ThrowAnExceptionIfTheEventIsNotAnSNSEvent()
        {
            var context = new MiddyNetContext(Substitute.For<ILambdaContext>());
            var middleware = new SNSTracingMiddleware<int, int>();

            await middleware.Before(1, context);

            context.MiddlewareExceptions.Should().HaveCount(1);
            context.MiddlewareExceptions.Single().Should().BeOfType<InvalidOperationException>();
        }

        [Fact]
        public async Task EnrichLoggerWithTraceContext()
        {
            var logger = Substitute.For<IMiddyLogger>();
            var context = new MiddyNetContext(Substitute.For<ILambdaContext>(), _ => logger);
            
            var middleware = new SNSTracingMiddleware<SNSEvent , int>();

            var snsEvent = new SNSEvent
            {
                Records = new List<SNSEvent.SNSRecord>()
                {
                    new SNSEvent.SNSRecord
                    {
                        Sns = new SNSEvent.SNSMessage
                        {
                            MessageAttributes = new Dictionary<string, SNSEvent.MessageAttribute>
                            {
                                {
                                    "traceparent",
                                    new SNSEvent.MessageAttribute {Type = "String", Value = "traceparent header value"}
                                },
                                {
                                    "tracestate",
                                    new SNSEvent.MessageAttribute {Type = "String", Value = "tracestate header value"}
                                }
                            }
                        }
                    }
                }
            };

            await middleware.Before(snsEvent, context);
            logger.Received().EnrichWith(Arg.Is<LogProperty>(p => p.Key == "traceparent"));
            logger.Received().EnrichWith(Arg.Is<LogProperty>(p => p.Key == "tracestate"));
        }
    }
}