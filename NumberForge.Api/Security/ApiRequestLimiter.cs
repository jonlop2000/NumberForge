using System.Net;
using System.Threading;

namespace NumberForge.Api.Security;

// A bounded set of per-IP counters prevents attacker-controlled addresses from growing memory forever.
public sealed class ApiRequestLimiter : IDisposable
{
    private readonly ApiSecurityOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<IPAddress, Counters> _clients = new Dictionary<IPAddress, Counters>();
    private readonly Lock _syncRoot = new Lock();
    private readonly SemaphoreSlim _concurrency;
    private long _windowStarted;
    private int _globalRequests;

    public ApiRequestLimiter(ApiSecurityOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        _windowStarted = clock.GetTimestamp();
        _concurrency = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
    }

    public bool TryAcquire(HttpContext context, out IDisposable? lease, out int retryAfterSeconds)
    {
        lease = null;
        lock (_syncRoot)
        {
            var elapsed = _clock.GetElapsedTime(_windowStarted);
            if (elapsed >= TimeSpan.FromMinutes(1))
            {
                // Resetting the fixed window also discards idle address entries.
                _clients.Clear();
                _globalRequests = 0;
                _windowStarted = _clock.GetTimestamp();
                elapsed = TimeSpan.Zero;
            }
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(60 - elapsed.TotalSeconds));
            if (_globalRequests >= _options.GlobalRequestsPerMinute)
            {
                return false;
            }
            _globalRequests++;

            var address = context.Connection.RemoteIpAddress ?? IPAddress.None;
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            if (!_clients.TryGetValue(address, out var counters))
            {
                if (_clients.Count >= _options.MaxClientBuckets)
                {
                    return false;
                }
                counters = new Counters();
                _clients.Add(address, counters);
            }

            bool mutation = !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
            // Routing accepts a trailing slash, so it must not bypass the tighter creation limit.
            string path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
            bool creation = HttpMethods.IsPost(context.Request.Method)
                && (path.Equals("/api/sessions", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/puzzles/generated", StringComparison.OrdinalIgnoreCase));
            if (counters.Requests >= _options.RequestsPerMinute
                || (mutation && counters.Mutations >= _options.MutationsPerMinute)
                || (creation && counters.Creations >= _options.CreationsPerMinute))
            {
                return false;
            }
            counters.Requests++;
            if (mutation) counters.Mutations++;
            if (creation) counters.Creations++;
        }

        // Reject excess work immediately; an unbounded waiting queue would only move the memory problem.
        if (!_concurrency.Wait(0))
        {
            retryAfterSeconds = 1;
            return false;
        }
        lease = new RequestLease(_concurrency);
        return true;
    }

    public void Dispose() => _concurrency.Dispose();

    private sealed class Counters
    {
        public int Requests;
        public int Mutations;
        public int Creations;
    }

    private sealed class RequestLease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
