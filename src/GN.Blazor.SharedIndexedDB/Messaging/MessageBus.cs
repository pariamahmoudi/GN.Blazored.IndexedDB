﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using GN.Blazor.SharedIndexedDB.SharedWorker;

namespace GN.Blazor.SharedIndexedDB.Messaging
{
    class MessageBus : IMessageBus
    {
        private readonly ILogger<MessageBus> logger;
        private readonly ISharedWorkerAdapter sharedWorkerAdapter;
        private readonly IServiceProvider provider;
        private Dictionary<string, List<Action<Message>>> subscriptions = new Dictionary<string, List<Action<Message>>>();
        private List<BusSubscription> _subscriptions = new List<BusSubscription>();
        private bool _started;
        private Dictionary<string, TaskCompletionSource<Message>> requests = new Dictionary<string, TaskCompletionSource<Message>>();
        public string Name { get; private set; }

        public MessageBus(ILogger<MessageBus> logger, ISharedWorkerAdapter sharedWorkerAdapter, IServiceProvider provider)
        {
            this.logger = logger;
            this.sharedWorkerAdapter = sharedWorkerAdapter;
            this.provider = provider;
            Name = "BlazorApp" + new Random().Next(1, 10000).ToString();
        }
        private async Task Start()
        {
            await Task.CompletedTask;
            if (!_started)
            {

                sharedWorkerAdapter.OnMessage += HandleSharedWorkerMessage;
                await sharedWorkerAdapter.StartServiceWorker();
                _started = true;
            }
        }
        private void HandleSharedWorkerMessage(Message message)
        {
            var context = new MessageContext(this, message, Transports.SharedWorker);
            _ = Publish(context, MessageScope.AllScopes.Except(context.Transport));

        }
        public async Task Publish(MessageContext ctx, MessageScope scope)
        {
            await Start();
            var message = ctx.Message;
            message.From = string.IsNullOrWhiteSpace(message.From) ? Name : message.From;
            if (message.IsReply())
            {
                if (requests.TryGetValue(message.ReplyTo, out var _s))
                {
                    if (message.IsError())
                    {
                        var fff = message.Payload?.ToString();
                        _s.SetException(new Exception(message.ToString()));
                    }
                    else
                    {
                        _s.SetResult(message);
                    }
                }
                return;
            }
            if (scope.Contains(Transports.SharedWorker))
            {
                await sharedWorkerAdapter.PostMessage(message);
            }
            if (scope.Contains(Transports.BusSubscriptions))
            {
                _subscriptions.Where(x => x.Matches(ctx))
                    .ToList()
                    .ForEach(x => x.Handle(ctx));
                //var handlers = this.subscriptions.TryGetValue(message.Subject, out var _handlers) ? _handlers : new List<Action<Message>>();
                //handlers.AddRange((this.subscriptions.TryGetValue("*", out var handlers1)) ? _handlers : new List<Action<Message>>());
                //handlers.ForEach(x => x(message));
            }
            //await this.sharedWorkerAdapter.PostMessage<object>(new Sh)
        }
        public async Task<Message> Request(MessageContext context, MessageScope scope)
        {
            await Start();
            var message = context.Message;
            var source = new TaskCompletionSource<Message>();
            requests[message.Id] = source;
            return await Publish(context, scope).ContinueWith(x => source.Task).Unwrap();
            //return this.PostMessage<T>(message).ContinueWith(x => source.Task).Unwrap();

        }
        public async Task Subscribe(string subject, Action<IMessageContext> handler, bool external = false)
        {
            await Start();
            _subscriptions = _subscriptions ?? new List<BusSubscription>();
            _subscriptions.Add(new BusSubscription(subject, handler));
            if (external)
            {
                await sharedWorkerAdapter.Subscribe(subject, HandleSharedWorkerMessage);
            }
            //if (!this.subscriptions.TryGetValue(subject, out _))
            //{
            //    this.subscriptions[subject] = new List<Action<Message>>();
            //}
            //this.subscriptions[subject].Add(handler);

        }

        public IMessageContext CreateContext(string subject, object message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            var _message = message as Message ?? new Message(subject, message);
            return new MessageContext(this, _message);
        }

        public IMessageContext CreateContext(Message message)
        {
            return new MessageContext(this, message);
        }
    }
}
