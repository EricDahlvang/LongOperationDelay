using AdaptiveExpressions.Properties;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace LongOperationDelay
{
    public class BackgroundThreadSleepDialog : Dialog
    {
        private const string TimedoutEventName = "timedOutEvent";
        private const string RepromptCount = "BackgroundThreadSleepDialogRepromptCount";

        public const string Kind = "BackgroundThreadSleepDialog";

        [JsonProperty("maxIgnoredMessages")]
        public IntExpression MaxIgnoredMessages { get; set; }

        [JsonProperty("sleepSeconds")]
        public IntExpression SleepSeconds { get; set; }

        [JsonProperty("prompt")]
        public ITemplate<Activity> Prompt { get; set; }

        public override async Task<DialogTurnResult> BeginDialogAsync(DialogContext dc, object options = null, CancellationToken cancellationToken = default)
        {
            // NOTE: we could reset the prompt count to zero when this dialog starts. However,
            // the count should in fact be for the entire conversation and if this dialog is loaded
            // multiple times, the ignored message count should not restart but should increment for the life
            // of the conversation.

            // Reset reprompt count to zero.
            //var state = dc.Context.TurnState.Get<ConversationState>();
            //var repromptCount = state.CreateProperty<int>($"{RepromptCount}{this.Id}");
            //await repromptCount.SetAsync(dc.Context, 0, cancellationToken).ConfigureAwait(false);
            
            // Retreive required values for the Background thread
            var claimsIdentity = dc.Context.TurnState.Get<IIdentity>(BotAdapter.BotIdentityKey) as ClaimsIdentity;
            var timeoutSeconds = SleepSeconds.GetValue(dc.State);
            var conversationReference = dc.Context.Activity.GetConversationReference();
            var continueActivity = new Activity { Type = ActivityTypes.Event, Name = TimedoutEventName }.ApplyConversationReference(conversationReference, true);

            // queue the event to be processed on a background thread.
            // This will ensure the thread sleep does not freeze the bot.
            // The continue event will close the dialog if it has not been closed already.
            var activityQueue = dc.Context.TurnState.Get<IActivityTaskQueue>();
            activityQueue.QueueBackgroundActivity(claimsIdentity, continueActivity, timeoutSeconds);

            return new DialogTurnResult(DialogTurnStatus.Waiting);
        }

        public override async Task<DialogTurnResult> ContinueDialogAsync(DialogContext dc, CancellationToken cancellationToken = default)
        {
            var state = dc.Context.TurnState.Get<ConversationState>();
            var repromptCountProperty = state.CreateProperty<int>($"{RepromptCount}{this.Id}");

            if(dc.Context.Activity.Type == ActivityTypes.Event)
            {
                return await HandleEventReceivedAsync(dc, repromptCountProperty, cancellationToken).ConfigureAwait(false);
            }
            else if (dc.Context.Activity.Type == ActivityTypes.Message)
            {
                return await HandleMessageReceivedAsync(dc, repromptCountProperty, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Received an unexpected activity type. Ignore and continue waiting
                return new DialogTurnResult(DialogTurnStatus.Waiting);
            }
        }

        private async Task<DialogTurnResult> HandleMessageReceivedAsync(DialogContext dc, IStatePropertyAccessor<int> repromptCountProperty, CancellationToken cancellationToken = default)
        {
            // Received a message so either re-send the prompt and increment the counter, or cancel

            var repromptCount = await repromptCountProperty.GetAsync(dc.Context, () => 0, cancellationToken).ConfigureAwait(false);
            if (repromptCount >= MaxIgnoredMessages.GetValue(dc.State))
            {
                await repromptCountProperty.DeleteAsync(dc.Context, cancellationToken).ConfigureAwait(false);
                return await dc.EndDialogAsync(null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (this.Prompt != null)
                {
                    var msg = await this.Prompt.BindAsync(dc, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await dc.Context.SendActivityAsync(msg, cancellationToken).ConfigureAwait(false);
                }

                await repromptCountProperty.SetAsync(dc.Context, repromptCount + 1, cancellationToken).ConfigureAwait(false);
                return new DialogTurnResult(DialogTurnStatus.Waiting);
            }
        }

        private async Task<DialogTurnResult> HandleEventReceivedAsync(DialogContext dc, IStatePropertyAccessor<int> repromptCountProperty, CancellationToken cancellationToken = default)
        {
            if (dc.Context.Activity.Name == TimedoutEventName)
            {
                await repromptCountProperty.DeleteAsync(dc.Context, cancellationToken).ConfigureAwait(false);
                return await dc.EndDialogAsync(null, cancellationToken).ConfigureAwait(false);
            }

            // not the expected timedout event, so continue waiting
            return new DialogTurnResult(DialogTurnStatus.Waiting);
        }
    }
}
