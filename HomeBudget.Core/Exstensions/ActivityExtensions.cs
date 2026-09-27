using System;
using System.Diagnostics;

namespace HomeBudget.Core.Observability;

public static class ActivityExtensions
{
    public static void SetTraceId(this Activity activity, string id)
    {
        activity?.SetTag(ActivityTags.TraceId, id);
    }

    public static void SetCorrelationId(this Activity activity, string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            activity?.AddBaggage(ActivityTags.CorrelationId, id);
        }

        activity?.SetTag(ActivityTags.CorrelationId, id);
    }

    public static void SetPayment(this Activity activity, Guid paymentId)
    {
        activity?.SetTag(ActivityTags.PaymentId, paymentId);
    }

    public static void SetAccount(this Activity activity, Guid accountId)
    {
        activity?.SetTag(ActivityTags.AccountId, accountId);
    }

    public static void RecordException(this Activity activity, Exception ex)
    {
        if (activity == null)
        {
            return;
        }

        activity.SetTag(ActivityTags.ExceptionType, ex?.GetType()?.FullName);
        activity.SetTag(ActivityTags.ExceptionMessage, ex.Message);
        activity.AddEvent(new ActivityEvent(
            "exception",
            tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.StackTrace }
            }));
        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
    }
}
