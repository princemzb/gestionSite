using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace GestionSite.Api.Tests;

/// <summary>Garde les événements journalisés par l'API, pour vérifier ce qu'elle a réellement fait.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(int EventId, string Message)> _messages = new();

    internal IReadOnlyCollection<(int EventId, string Message)> Entries => _messages;

    public ILogger CreateLogger(string categoryName) => new Recorder(_messages);

    public void Dispose()
    {
        // Rien à libérer : les messages restent lisibles après l'arrêt de l'hôte.
    }

    private sealed class Recorder(ConcurrentQueue<(int EventId, string Message)> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue((eventId.Id, formatter(state, exception)));
    }
}
