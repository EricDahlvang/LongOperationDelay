using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace LongOperationDelay
{
    public class ProactiveContinueAdapter : CloudAdapter
    {
        IBot _bot;

        public ProactiveContinueAdapter(
            BotFrameworkAuthentication botFrameworkAuthentication,
            IEnumerable<Microsoft.Bot.Builder.IMiddleware> middlewares,
            IBot bot,
            IActivityTaskQueue taskQueue,
            ILogger logger = null)
            : base(botFrameworkAuthentication, logger)
        {
            _bot = bot;

            // Pick up feature based middlewares such as telemetry or transcripts
            foreach (Microsoft.Bot.Builder.IMiddleware middleware in middlewares)
            {
                Use(middleware);
            }
            
            Use(new RegisterClassMiddleware<IActivityTaskQueue>(taskQueue ?? throw new ArgumentNullException(nameof(taskQueue))));
            
            OnTurnError = async (turnContext, exception) =>
            {
                // Log any leaked exception from the application.
                Logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

                // Send the exception message to the user. Since the default behavior does not
                // send logs or trace activities, the bot appears hanging without any activity
                // to the user.
                await turnContext.SendActivityAsync(exception.Message).ConfigureAwait(false);

                var conversationState = turnContext.TurnState.Get<ConversationState>();

                if (conversationState != null)
                {
                    // Delete the conversationState for the current conversation to prevent the
                    // bot from getting stuck in a error-loop caused by being in a bad state.
                    await conversationState.DeleteAsync(turnContext).ConfigureAwait(false);
                }
            };
        }

        public Task ProactiveContinueAsync(ClaimsIdentity claimsIdentity, Activity continuationActivity, string audience, CancellationToken cancellationToken)
        {
            return base.ProcessProactiveAsync(claimsIdentity, continuationActivity, audience, _bot.OnTurnAsync, cancellationToken);
        }
    }
}
