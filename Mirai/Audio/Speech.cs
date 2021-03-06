﻿using Discord.Audio;
using Microsoft.Speech.Recognition;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mirai.Audio
{
    class Speech
    {
        private static ConcurrentDictionary<ulong, CancellationTokenSource> Cancel = new ConcurrentDictionary<ulong, CancellationTokenSource>();

        internal static async Task StartListenService(ulong User, AudioInStream In)
        {
            StopListenService(User);
            var Source = new CancellationTokenSource();
            if (Cancel.TryAdd(User, Source))
            {
                var Queue = new Queue<RTPFrame>();
                var Timer = new Timer(e =>
                {
                    if (!Source.IsCancellationRequested)
                        ProcessVoiceAsync(User, Queue.ToArray()).ConfigureAwait(false);

                    Queue.Clear();
                }, null, Timeout.Infinite, Timeout.Infinite);

                while (!Source.IsCancellationRequested)
                    try
                    {
                        Queue.Enqueue(await In.ReadFrameAsync(Source.Token));
                        Timer.Change(125, 0);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception Ex)
                    {
                        Logger.Log(Ex);
                    }
            }
        }

        private static async Task ProcessVoiceAsync(ulong UserId, RTPFrame[] Frames)
        {
            try
            {
                RecognizeCompletedEventArgs Args;
                using (var Stream = new MemoryStream())
                {
                    for (int i = 0; i < Frames.Length; i++)
                        await Stream.WriteAsync(Frames[i].Payload, 0, Frames[i].Payload.Length);

                    Stream.Position = 0;

                    var RecognizeWaiter = new TaskCompletionSource<RecognizeCompletedEventArgs>();
                    using (var Engine = await SpeechEngine.Get((s, e) => RecognizeWaiter.SetResult(e)))
                    {
                        Engine.Recognize(Stream);
                        Args = await RecognizeWaiter.Task;
                    }
                }

                if (Args.Result?.Text != null)
                {
                    Logger.Log($"{UserId} said {Args.Result.Text} {Args.Result.Confidence} confidence");

                    if (Args.Result.Confidence >= User.GetConfidence(UserId))
                    {
                        var Values = new Queue<string>(Args.Result.Words.Select(x => x.Text).ToArray());
                        for (int i = 0; i < SpeechEngine.Trigger.Length; i++)
                            Values.Dequeue();

                        var Rank = User.GetRank(UserId);
                        var Cmd = Command.GetVoice(string.Join(" ", Values), Rank);

                        if (Cmd == null)
                            Command.GetVoice(Values.Dequeue(), Rank)?.Invoke(UserId, Values);
                        else
                        {
                            Values.Clear();
                            Cmd.Invoke(UserId, Values);
                        }
                    }
                }
            }
            catch (Exception Ex)
            {
                Logger.Log(Ex);
            }
        }

        internal static async Task StopListenService(ulong User)
        {
            if (Cancel.TryRemove(User, out CancellationTokenSource Source))
                Source.Cancel();
        }
    }
}
