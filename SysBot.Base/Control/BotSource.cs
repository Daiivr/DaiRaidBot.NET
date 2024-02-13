﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base
{
    public class BotSource<T> where T : class, IConsoleBotConfig
    {
        public readonly RoutineExecutor<T> Bot;
        private CancellationTokenSource Source = new();

        public BotSource(RoutineExecutor<T> bot) => Bot = bot;

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }

        private bool IsStopping { get; set; }
        // Retry connection if bot crashes
        private int retryCount = 0;
        private DateTime firstFailureTime;
        private bool isFirstFailure = true;
        public void Stop()
        {
            if (!IsRunning || IsStopping)
                return;

            IsStopping = true;
            Source.Cancel();
            Source = new CancellationTokenSource();

            Task.Run(async () =>
            {
                IsPaused = IsRunning = IsStopping = false;
                await Bot.HardStop().ConfigureAwait(false);
            });
        }

        public void Pause()
        {
            if (!IsRunning || IsStopping)
                return;

            IsPaused = true;
            Bot.SoftStop();
        }

        public void Start()
        {
            if (IsPaused)
                Stop(); // can't soft-resume; just re-launch

            if (IsRunning || IsStopping)
                return;

            Task.Run(() => Bot.RunAsync(Source.Token)
                .ContinueWith(ReportFailure, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)
                .ContinueWith(_ => IsRunning = false));

            IsRunning = true;
        }

        private void ReportFailure(Task finishedTask)
        {
            // Initialize firstFailureTime during the first failure
            if (isFirstFailure)
            {
                firstFailureTime = DateTime.Now;
                isFirstFailure = false;
            }
            var ident = Bot.Connection.Name;
            var ae = finishedTask.Exception;
            if (ae == null)
            {
                LogUtil.LogError("Bot has stopped without error.", ident);
                return;
            }

            LogUtil.LogError("Bot has crashed!", ident);

            if (!string.IsNullOrEmpty(ae.Message))
                LogUtil.LogError("Aggregate message: " + ae.Message, ident);

            var st = ae.StackTrace;
            if (!string.IsNullOrEmpty(st))
                LogUtil.LogError("Aggregate stacktrace: " + st, ident);

            foreach (var e in ae.InnerExceptions)
            {
                if (!string.IsNullOrEmpty(e.Message))
                    LogUtil.LogError("Inner message: " + e.Message, ident);
                LogUtil.LogError("Inner stacktrace: " + e.StackTrace, ident);
            }
            // Check if 10 minutes have passed since the first failure
            if ((DateTime.Now - firstFailureTime).TotalMinutes >= 10)
            {
                retryCount = 0;
                firstFailureTime = DateTime.Now;
            }

            // Check if the number of retry attempts is less than 5
            if (retryCount < 5)
            {
                // Increment the retry count
                retryCount++;

                // Restart the bot
                Start();
            }
            else
            {
                LogUtil.LogError("Maximum number of retry attempts reached. Not restarting.", Bot.Connection.Name);
            }
        }



        public void Resume()
        {
            Start();
        }
    }
}
