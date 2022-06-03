﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LongOperationDelay
{
    /// <summary>
    /// <see cref="BackgroundService"/> implementation used to process activities with trusted claims, using a background service.
    ///  <see href="https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.backgroundservice">More information.</see>
    /// </summary>
    public class HostedActivityService : BackgroundService
    {
        private readonly ILogger<HostedActivityService> _logger;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly ConcurrentDictionary<ActivityWithTrustedClaims, Task> _activitiesProcessing = new ConcurrentDictionary<ActivityWithTrustedClaims, Task>();
        private IActivityTaskQueue _activityQueue;
        private readonly ProactiveContinueAdapter _adapter;
        private readonly IBot _bot;
        private readonly int _shutdownTimeoutSeconds;

        /// <summary>
        /// Create a <see cref="HostedActivityService"/> instance for processing Bot Framework Activities
        /// on background threads.
        /// </summary>
        /// <remarks>
        /// It is important to note that exceptions on the background thread are only logged in the <see cref="ILogger"/>.
        /// </remarks>
        /// <param name="config"><see cref="IConfiguration"/> used to retrieve ShutdownTimeoutSeconds from appsettings.</param>
        /// <param name="adapter"><see cref="ProactiveContinueAdapter"/> used to process Activities. </param>
        /// <param name="activityTaskQueue"><see cref="IActivityTaskQueue"/>Queue of activities to be processed.  This class
        /// contains a semaphore which the BackgroundService waits on to be notified of activities to be processed.</param>
        /// <param name="logger">Logger to use for logging BackgroundService processing and exception information.</param>
        public HostedActivityService(IConfiguration config, IBot bot, ProactiveContinueAdapter adapter, IActivityTaskQueue activityTaskQueue, ILogger<HostedActivityService> logger)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (bot == null)
            {
                throw new ArgumentNullException(nameof(bot));
            }

            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            if (activityTaskQueue == null)
            {
                throw new ArgumentNullException(nameof(activityTaskQueue));
            }

            _shutdownTimeoutSeconds = config.GetValue<int>("ShutdownTimeoutSeconds", 30);
            _activityQueue = activityTaskQueue;
            _bot = bot;
            _adapter = adapter;
            _logger = logger ?? NullLogger<HostedActivityService>.Instance;
        }

        /// <summary>
        /// Called by BackgroundService when the hosting service is shutting down.
        /// </summary>
        /// <param name="stoppingToken"><see cref="CancellationToken"/> sent from BackgroundService for shutdown.</param>
        /// <returns>The Task to be executed asynchronously.</returns>
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queued Hosted Service is stopping.");

            // Obtain a write lock and do not release it, preventing new tasks from starting
            if (_lock.TryEnterWriteLock(TimeSpan.FromSeconds(_shutdownTimeoutSeconds)))
            {
                // Wait for currently running tasks, but only n seconds.
                await Task.WhenAny(Task.WhenAll(_activitiesProcessing.Values), Task.Delay(TimeSpan.FromSeconds(_shutdownTimeoutSeconds)));
            }

            await base.StopAsync(stoppingToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Queued Hosted Service is running.{Environment.NewLine}");

            await BackgroundProcessing(stoppingToken);
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var activityWithClaims = await _activityQueue.WaitForActivityAsync(stoppingToken);
                if (activityWithClaims != null)
                {
                    try
                    {
                        // The read lock will not be acquirable if the app is shutting down.
                        // New tasks should not be starting during shutdown.
                        if (_lock.TryEnterReadLock(500))
                        {
                            // Create the task which will execute the work item.
                            var task = GetTaskFromWorkItem(activityWithClaims, stoppingToken)
                                .ContinueWith(t =>
                                {
                                    // After the work item completes, clear the running tasks of all completed tasks.
                                    foreach (var task in _activitiesProcessing.Where(tsk => tsk.Value.IsCompleted))
                                    {
                                        _activitiesProcessing.TryRemove(task.Key, out Task removed);
                                    }
                                }, stoppingToken);

                            _activitiesProcessing.TryAdd(activityWithClaims, task);
                        }
                        else
                        {
                            _logger.LogError("Work item not processed.  Server is shutting down.", nameof(BackgroundProcessing));
                        }
                    }
                    finally
                    {
                        _lock.ExitReadLock();
                    }
                }
            }
        }

        private Task GetTaskFromWorkItem(ActivityWithTrustedClaims activityWithClaims, CancellationToken stoppingToken)
        {
            // Start the work item, and return the task
            return Task.Run(
                async () =>
                {
                    try
                    {
                        if (activityWithClaims.SleepSeconds.HasValue && activityWithClaims.SleepSeconds > 0)
                        {
                            Thread.Sleep(activityWithClaims.SleepSeconds.Value * 1000);
                        }

                        var claimsIdentity = activityWithClaims.ClaimsIdentity;
                        var audience = SkillValidation.IsSkillClaim(claimsIdentity.Claims) ? JwtTokenValidation.GetAppIdFromClaims(claimsIdentity.Claims) : AuthenticationConstants.ToChannelFromBotOAuthScope;
                        await _adapter.ProactiveContinueAsync(activityWithClaims.ClaimsIdentity, activityWithClaims.Activity, audience, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // Bot Errors should be processed in the Adapter.OnTurnError.
                        _logger.LogError(ex, "Error occurred executing WorkItem.", nameof(HostedActivityService));
                        throw;
                    }
                }, stoppingToken);
        }
    }
}