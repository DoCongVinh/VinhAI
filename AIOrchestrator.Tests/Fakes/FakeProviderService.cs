using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIOrchestrator.Models;
using AIOrchestrator.Services;

namespace AIOrchestrator.Tests.Fakes
{
    /// <summary>
    /// Deterministic in-memory <see cref="IAiProviderService"/> test double.
    ///
    /// Responses are consumed in order from a queue. If a queued item is an
    /// <see cref="Exception"/> it is thrown instead of returned, which lets a
    /// test simulate a provider failure on a specific call.
    /// </summary>
    public sealed class FakeProviderService : IAiProviderService
    {
        private readonly Queue<object> _scripted = new();

        /// <summary>Every prompt this provider was asked to generate a response for, in order.</summary>
        public List<PromptCall> Calls { get; } = new();

        /// <summary>Default text returned once the scripted queue is empty.</summary>
        public string DefaultResponse { get; set; } = "DEFAULT_RESPONSE";

        /// <summary>Optional behavior invoked for every call, before scripted items are considered.</summary>
        public Func<AiAgent, string, string>? ResponseFactory { get; set; }

        /// <summary>Optional hook to observe/act on cancellation before returning.</summary>
        public Action<CancellationToken>? BeforeReturn { get; set; }

        public int CallCount => Calls.Count;

        /// <summary>Queue a successful response.</summary>
        public FakeProviderService EnqueueResponse(string response)
        {
            _scripted.Enqueue(response);
            return this;
        }

        /// <summary>Queue an exception to be thrown on the corresponding call.</summary>
        public FakeProviderService EnqueueException(Exception exception)
        {
            _scripted.Enqueue(exception);
            return this;
        }

        public Task<string> GenerateResponseAsync(AiAgent agent, string prompt, AppSettings settings, CancellationToken ct = default)
        {
            Calls.Add(new PromptCall(agent, prompt));

            ct.ThrowIfCancellationRequested();
            BeforeReturn?.Invoke(ct);

            if (_scripted.Count > 0)
            {
                object next = _scripted.Dequeue();
                if (next is Exception ex)
                {
                    return Task.FromException<string>(ex);
                }
                return Task.FromResult((string)next);
            }

            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(agent, prompt));
            }

            return Task.FromResult(DefaultResponse);
        }

        public readonly record struct PromptCall(AiAgent Agent, string Prompt);
    }
}
