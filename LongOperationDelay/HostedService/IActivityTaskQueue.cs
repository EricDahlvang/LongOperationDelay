using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace LongOperationDelay
{
    /// <summary>
    /// Interface for a class used to transfer an ActivityWithTrustedClaims to the <see cref="HostedActivityService"/>.
    /// </summary>
    public interface IActivityTaskQueue
    {
        /// <summary>
        /// Enqueue an Activity, with ClaimsIdentity, to be processed on a background thread.
        /// </summary>
        /// <remarks>
        /// It is assumed the ClaimsIdentity has been authenticated via BotFrameworkAuthentication.AuthenticateRequestAsync 
        /// before enqueueing.
        /// </remarks>
        /// <param name="claims">Authenticated <see cref="ClaimsIdentity"/> used to process the 
        /// activity.</param>
        /// <param name="activity"><see cref="Activity"/> to be processed.</param>
        /// <param name="sleepSeconds">Number of seconds to sleep before processing the activity.</pa
        void QueueBackgroundActivity(ClaimsIdentity claims, Activity activity, int? sleepSeconds = null);

        /// <summary>
        /// Wait for a signal of an enqueued Activity with Claims to be processed.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken used to cancel the wait.</param>
        /// <returns>An ActivityWithTrustedClaims to be processed.</returns>
        /// <remarks>It is assumed these claims have already been authenticated.</remarks>
        Task<ActivityWithTrustedClaims> WaitForActivityAsync(CancellationToken cancellationToken);
    }
}