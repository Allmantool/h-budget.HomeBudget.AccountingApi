using System.Net;

using FluentAssertions;
using RestSharp;

using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.IntegrationTests.Extensions
{
    internal static class RestResponseAssertionExtensions
    {
        public static Result<T> ShouldBeHttpFailureWithDomainFailure<T>(
            this RestResponse<Result<T>> response,
            HttpStatusCode expectedStatusCode,
            string context)
        {
            response.Should().NotBeNull(context);
            response.StatusCode.Should().Be(expectedStatusCode, DescribeResponse(response, context));
            response.IsSuccessful.Should().BeFalse(DescribeResponse(response, context));
            response.Data.Should().NotBeNull(DescribeResponse(response, context));
            response.Data.IsSucceeded.Should().BeFalse(DescribeResponse(response, context));
            response.Data.StatusMessage.Should().NotBeNullOrWhiteSpace(DescribeResponse(response, context));

            return response.Data;
        }

        public static Result<T> ShouldBeHttpSuccessWithDomainSuccess<T>(
            this RestResponse<Result<T>> response,
            string context)
        {
            response.Should().NotBeNull(context);
            response.IsSuccessful.Should().BeTrue(DescribeResponse(response, context));
            response.Data.Should().NotBeNull(DescribeResponse(response, context));
            response.Data.IsSucceeded.Should().BeTrue(DescribeResponse(response, context));

            return response.Data;
        }

        public static string DescribeResponse<T>(
            this RestResponse<Result<T>> response,
            string context = null)
        {
            if (response == null)
            {
                return $"{context ?? "HTTP response"}: response was null.";
            }

            return $"{context ?? "HTTP response"}: HTTP {(int)response.StatusCode} {response.StatusCode}, " +
                   $"transport-success={response.IsSuccessful}, rest-error='{response.ErrorMessage}', " +
                   $"domain-success={response.Data?.IsSucceeded}, status='{response.Data?.StatusMessage}', " +
                   $"content='{response.Content}'";
        }
    }
}
