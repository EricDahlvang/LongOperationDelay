using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using System;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace LongOperationDelay
{
    /// <summary>
    /// Singleton queue, used to transfer an ActivityWithTrustedClaims to the <see cref="HostedActivityService"/>.
    /// </summary>
    public class ActivityTaskQueue : IActivityTaskQueue
    {
        private SemaphoreSlim _signal = new SemaphoreSlim(0);
        private ConcurrentQueue<ActivityWithTrustedClaims> _activities = new ConcurrentQueue<ActivityWithTrustedClaims>();

        /// <summary>
        /// Enqueue an Activity, with ClaimsIdentity, to be processed on a background thread.
        /// </summary>
        /// <remarks>
        /// It is assumed these ClaimsIdentity has been authenticated via BotFrameworkAuthentication.AuthenticateRequestAsync 
        /// before enqueueing.
        /// </remarks>
        /// <param name="claimsIdentity">Authenticated <see cref="ClaimsIdentity"/> used to process the 
        /// activity.</param>
        /// <param name="activity"><see cref="Activity"/> to be processed.</param>
        /// <param name="sleepSeconds">Number of seconds to sleep before processing the activity.</param>
        public void QueueBackgroundActivity(ClaimsIdentity claimsIdentity, Activity activity, int? sleepSeconds = null)
        {
            if (claimsIdentity == null)
            {
                throw new ArgumentNullException(nameof(claimsIdentity));
            }

            if (activity == null)
            {
                throw new ArgumentNullException(nameof(activity));
            }

            _activities.Enqueue(new ActivityWithTrustedClaims { ClaimsIdentity = claimsIdentity, Activity = activity, SleepSeconds = sleepSeconds });
            _signal.Release();
        }

        /// <summary>
        /// Wait for a signal of an enqueued Activity with Claims to be processed.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken used to cancel the wait.</param>
        /// <returns>An ActivityWithAuthenticateRequestResult to be processed.
        /// </returns>
        /// <remarks>It is assumed these claims have already been authenticated.</remarks>
        public async Task<ActivityWithTrustedClaims> WaitForActivityAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);

            ActivityWithTrustedClaims dequeued;
            _activities.TryDequeue(out dequeued);

            return dequeued;
        }
    }
}