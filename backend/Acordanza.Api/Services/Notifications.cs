namespace Acordanza.Api.Services;

// A delivery failure must not fail the decision that triggered it — every
// approve/reject notification email goes through here instead of a
// hand-rolled try/catch + LogWarning per call site.
public static class Notifications
{
    public static async Task BestEffortAsync(ILogger logger, Func<Task> send, string failureMessage, params object?[] args)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, failureMessage, args);
        }
    }
}
