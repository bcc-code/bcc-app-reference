using Wolverine;
using Wolverine.ErrorHandling;

namespace BccCode.Template.Web.Messaging;

/// <summary>
/// What a handler failure on a pushed message tells Pub/Sub. The push receiver (<c>POST /internal/pubsub/{subscription}</c>)
/// answers 204 when the handler succeeded and 500 when the error policy requeues, so Pub/Sub redelivers with its own backoff
/// and moves the message to the dead-letter topic after <see cref="FinalAttempt"/> deliveries. A message that cannot be read
/// is moved to Wolverine's dead letter storage (kept 7 days) and acked.
/// </summary>
/// <remarks>Under <c>InvokeAsync</c> a requeue has no inline form, so the exception is rethrown to the caller.</remarks>
public static class PushFailurePolicies
{
    /// <summary>The push subscription's <c>max_delivery_attempts</c>. Terraform sets the same number.</summary>
    public const int FinalAttempt = 5;

    public static void Apply(WolverineOptions options) => options.Policies.OnAnyException().RequeueIndefinitely();
}
