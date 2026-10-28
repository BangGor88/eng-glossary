using System.Net;
using System.Net.Http.Headers;

namespace EnglishWords.Infrastructure.Dictionaries;

// Paces requests and retries rate-limited (429) responses so a burst of lookups does not get blocked.
internal sealed class WiktionaryApiClient(HttpClient httpClient, string siteName)
{
    private const int MaximumRetries = 6;
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task<HttpResponseMessage> GetAsync(string uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await WaitForTurnAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnglishWordsApp", "1.0"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(https://github.com/BangGor88/eng-glossary; vocabulary practice app)"));
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            if (attempt >= MaximumRetries)
            {
                response.Dispose();
                throw new HttpRequestException(
                    $"{siteName} is rate-limiting requests (429). Wait a minute and retry.",
                    null,
                    HttpStatusCode.TooManyRequests);
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            response.Dispose();
            await Task.Delay(delay > MaximumRetryDelay ? MaximumRetryDelay : delay, cancellationToken);
        }
    }

    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequestAt + MinimumRequestInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
