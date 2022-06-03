using System.Security.Claims;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;

namespace LongOperationDelay
{
    /// <summary>
    /// Activity with Claims which should already have been authenticated.
    /// </summary>
    public class ActivityWithTrustedClaims
    {
        /// <summary>
        /// <see cref="ClaimsIdentity"/> retrieved from a call to BotFrameworkAuthentication.AuthenticateRequestAsync.
        /// <seealso cref="ImmediateAcceptAdapter"/>
        /// </summary>
        public ClaimsIdentity ClaimsIdentity { get; set; }

        /// <summary>
        /// <see cref="Activity"/> which is to be processed.
        /// </summary>
        public Activity Activity { get; set; }

        /// <summary>
        /// Number of seconds to sleep before processing the activity.
        /// </summary>
        public int? SleepSeconds { get; set; }


    }
}