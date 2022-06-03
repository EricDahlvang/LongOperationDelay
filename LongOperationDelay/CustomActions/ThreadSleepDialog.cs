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
    public class ThreadSleepDialog : Dialog
    {
        private const string TimedoutEventName = "timedOutEvent";
        private const string RepromptCount = "ThreadSleepDialogRepromptCount";

        public const string Kind = "ThreadSleepDialog";

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
            
            // Retreive required values for the Timeout thread
            var adapter = dc.Context.Adapter as ProactiveContinueAdapter;
            var claimsIdentity = dc.Context.TurnState.Get<IIdentity>(BotAdapter.BotIdentityKey) as ClaimsIdentity;
            var timeoutSeconds = SleepSeconds.GetValue(dc.State);
            var conversationReference = dc.Context.Activity.GetConversationReference();

            // Do not await this call. It will sleep the expected number of seconds, and trigger a TimedoutEventName event
            // which will close this dialog if it is not closed already.
            Task.Factory.StartNew(async () =>
            {
                Thread.Sleep(timeoutSeconds * 1000);
                ProcessTimeoutEvent(adapter, claimsIdentity, conversationReference);
            });

            return new DialogTurnResult(DialogTurnStatus.Waiting);
        }

        private async Task ProcessTimeoutEvent(ProactiveContinueAdapter adapter, ClaimsIdentity claimsIdentity, ConversationReference reference, CancellationToken cancellationToken = default)
        {
            // Send the adapter an Event activity which will be processed by ContinueDialogAsync, if this dialog is still on the stack.
            var continueActivity = new Activity { Type = ActivityTypes.Event, Name = TimedoutEventName }.ApplyConversationReference(reference, true);
            var audience = SkillValidation.IsSkillClaim(claimsIdentity.Claims) ? JwtTokenValidation.GetAppIdFromClaims(claimsIdentity.Claims) : AuthenticationConstants.ToChannelFromBotOAuthScope;

            await adapter.ProactiveContinueAsync(claimsIdentity, continueActivity, audience, cancellationToken).ConfigureAwait(false);
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
