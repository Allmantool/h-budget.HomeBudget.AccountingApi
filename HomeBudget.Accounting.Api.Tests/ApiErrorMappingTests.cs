using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Confluent.Kafka;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

using HomeBudget.Accounting.Api.Filters;
using HomeBudget.Accounting.Api.Middlewares;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public class ApiErrorMappingTests
    {
        [TestCase("Validation failed: Amount must not be zero", HttpStatusCode.BadRequest)]
        [TestCase("Invalid payment reference 'categoryId' has been provided: 'abc'", HttpStatusCode.BadRequest)]
        [TestCase("The category 'c77597b2-8da5-4c3e-92c9-fbb7a5607964' hasn't been found", HttpStatusCode.NotFound)]
        [TestCase("The contractor with 'groceries' key already exists", HttpStatusCode.Conflict)]
        [TestCase("", HttpStatusCode.ServiceUnavailable)]
        public void FromFailureMessage_ShouldMapExpectedStatus(string message, HttpStatusCode expectedStatus)
        {
            ApiErrorStatusCodeClassifier.FromFailureMessage(message).Should().Be((int)expectedStatus);
        }

        [Test]
        public void FromException_WhenKafkaOrEventStoreUnavailable_ShouldMapServiceUnavailable()
        {
            var kafkaException = new KafkaException(new Error(ErrorCode.Local_AllBrokersDown, "broker details"));
            var eventStoreException = new RpcException(new Status(StatusCode.Unavailable, "eventstore details"));

            ApiErrorStatusCodeClassifier.FromException(kafkaException).Should().Be(StatusCodes.Status503ServiceUnavailable);
            ApiErrorStatusCodeClassifier.FromException(eventStoreException).Should().Be(StatusCodes.Status503ServiceUnavailable);
        }

        [Test]
        public async Task ResultToHttpStatusFilter_WhenFailureResult_ShouldSetNonSuccessStatus()
        {
            var filter = new ResultToHttpStatusFilter();
            var objectResult = new ObjectResult(Result<Guid>.Failure("The payment account 'missing' hasn't been found"));
            var context = FilterTestContextFactory.CreateResultExecutingContext(objectResult);

            await filter.OnResultExecutionAsync(
                context,
                () => Task.FromResult(FilterTestContextFactory.CreateResultExecutedContext(context)));

            objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        }

        [Test]
        public async Task ApiExceptionHandlingMiddleware_WhenOutboxWriteThrows_ShouldReturnFailureResponse()
        {
            var httpContext = new DefaultHttpContext
            {
                Response =
                {
                    Body = new MemoryStream()
                }
            };

            var middleware = new ApiExceptionHandlingMiddleware(
                _ => throw new InvalidOperationException("SQL password is secret"),
                NullLogger<ApiExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(httpContext);

            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

            httpContext.Response.Body.Position = 0;
            var response = await JsonSerializer.DeserializeAsync<Result<object>>(
                httpContext.Response.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            response.IsSucceeded.Should().BeFalse();
            response.StatusMessage.Should().Be("An unexpected error occurred");
            response.StatusMessage.Should().NotContain("secret");
        }

        [Test]
        public async Task ApiExceptionHandlingMiddleware_WhenInfrastructureUnavailable_ShouldReturnServiceUnavailable()
        {
            var httpContext = new DefaultHttpContext
            {
                Response =
                {
                    Body = new MemoryStream()
                }
            };

            var middleware = new ApiExceptionHandlingMiddleware(
                _ => throw new TimeoutException("Kafka broker unavailable"),
                NullLogger<ApiExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(httpContext);

            httpContext.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

            httpContext.Response.Body.Position = 0;
            var response = await JsonSerializer.DeserializeAsync<Result<object>>(
                httpContext.Response.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            response.IsSucceeded.Should().BeFalse();
            response.StatusMessage.Should().Be("Infrastructure dependency is unavailable");
            response.StatusMessage.Should().NotContain("Kafka broker unavailable");
        }
    }
}
