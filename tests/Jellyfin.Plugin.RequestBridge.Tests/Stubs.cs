using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestBridge.Tests;

/// <summary>
/// A logger that discards everything.
/// </summary>
/// <remarks>
/// Hand-written rather than pulled from a mocking library. These tests need a
/// logger to exist, not to be observed, and a dependency earns its place by
/// removing more than it adds.
/// </remarks>
/// <typeparam name="T">The category type.</typeparam>
internal sealed class StubLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Deliberately empty.
    }
}

/// <summary>
/// A client factory that hands out an unconfigured client.
/// </summary>
/// <remarks>
/// Sufficient for tests that never make a call. Anything that would actually
/// reach the network belongs in an integration test against a real provider,
/// not here.
/// </remarks>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
