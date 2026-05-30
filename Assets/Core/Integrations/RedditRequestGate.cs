using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class RedditRequestGate
{
    private static readonly object Sync = new object();
    private static DateTime nextRequestUtc = DateTime.MinValue;
    private static DateTime blockedUntilUtc = DateTime.MinValue;
    private static float minSecondsBetweenRequests = 6f;
    private static float rateLimitCooldownSeconds = 300f;
    private static int maxAttempts = 3;
    private static string clientId;
    private static string clientSecret;
    private static string deviceId;
    private static string userAgent = "script:hbox:1.1 (by /u/Akrivus)";
    private static string accessToken;
    private static DateTime accessTokenExpiresUtc = DateTime.MinValue;

    public static void Configure(
        float spacingSeconds,
        float cooldownSeconds,
        int attempts,
        string oauthClientId,
        string oauthClientSecret,
        string oauthDeviceId,
        string oauthUserAgent)
    {
        lock (Sync)
        {
            minSecondsBetweenRequests = Mathf.Max(1f, spacingSeconds);
            rateLimitCooldownSeconds = Mathf.Max(30f, cooldownSeconds);
            maxAttempts = Mathf.Max(1, attempts);
            clientId = string.IsNullOrWhiteSpace(oauthClientId) ? null : oauthClientId.Trim();
            clientSecret = string.IsNullOrWhiteSpace(oauthClientSecret) ? null : oauthClientSecret.Trim();
            deviceId = string.IsNullOrWhiteSpace(oauthDeviceId) ? "DO_NOT_TRACK_THIS_DEVICE" : oauthDeviceId.Trim();
            userAgent = string.IsNullOrWhiteSpace(oauthUserAgent) ? userAgent : oauthUserAgent.Trim();
            accessToken = null;
            accessTokenExpiresUtc = DateTime.MinValue;
        }
    }

    public static string DownloadString(WebClient client, string url)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            WaitForSlot();

            try
            {
                PrepareClient(client);
                return client.DownloadString(ToApiUrl(url));
            }
            catch (WebException e) when (TryGetRateLimitDelay(e, out var retryDelay))
            {
                ApplyCooldown(retryDelay);
                if (attempt >= maxAttempts)
                {
                    throw new RedditRateLimitException(
                        $"Reddit request blocked or rate-limited after {attempt} attempt{(attempt == 1 ? "" : "s")}; cooling down for {retryDelay.TotalSeconds:0.#}s.",
                        e);
                }

                Debug.LogWarning($"Reddit request blocked or rate-limited; retrying attempt {attempt + 1}/{maxAttempts} after {retryDelay.TotalSeconds:0.#}s.");
            }
        }

        throw new RedditRateLimitException("Reddit request failed after all retry attempts.", null);
    }

    private static void PrepareClient(WebClient client)
    {
        client.Headers[HttpRequestHeader.UserAgent] = userAgent;
        if (string.IsNullOrWhiteSpace(clientId))
            return;

        client.Headers[HttpRequestHeader.Authorization] = "Bearer " + GetAccessToken();
    }

    private static string ToApiUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return url;

        return url.Replace("https://www.reddit.com", "https://oauth.reddit.com");
    }

    private static string GetAccessToken()
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(accessToken) && accessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(1))
                return accessToken;

            using var tokenClient = new TimeoutWebClient();
            tokenClient.Headers[HttpRequestHeader.UserAgent] = userAgent;
            tokenClient.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
            tokenClient.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret ?? string.Empty}"));

            var grantType = string.IsNullOrWhiteSpace(clientSecret)
                ? "https://oauth.reddit.com/grants/installed_client"
                : "client_credentials";
            var body = $"grant_type={Uri.EscapeDataString(grantType)}";
            if (string.IsNullOrWhiteSpace(clientSecret))
                body += $"&device_id={Uri.EscapeDataString(deviceId)}";

            string json;
            try
            {
                json = tokenClient.UploadString("https://www.reddit.com/api/v1/access_token", "POST", body);
            }
            catch (WebException e)
            {
                throw new RedditRateLimitException($"Reddit OAuth token request failed: {DescribeWebException(e)}", e);
            }

            var token = JObject.Parse(json);
            accessToken = token.Value<string>("access_token");
            var expiresIn = token.Value<int?>("expires_in") ?? 3600;
            accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Mathf.Max(60, expiresIn - 60));

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new RedditRateLimitException("Reddit OAuth token response did not include an access token.", null);

            return accessToken;
        }
    }

    private static string DescribeWebException(WebException exception)
    {
        if (exception.Response is not HttpWebResponse response)
            return exception.Message;

        try
        {
            using var stream = response.GetResponseStream();
            if (stream == null)
                return $"{(int)response.StatusCode} {response.StatusCode}";
            using var reader = new StreamReader(stream);
            var body = reader.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(body))
                return $"{(int)response.StatusCode} {response.StatusCode}: {body}";
        }
        catch
        {
        }

        return $"{(int)response.StatusCode} {response.StatusCode}";
    }

    private static void WaitForSlot()
    {
        while (true)
        {
            TimeSpan wait;
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                var target = nextRequestUtc > blockedUntilUtc ? nextRequestUtc : blockedUntilUtc;
                if (target <= now)
                {
                    nextRequestUtc = now.AddSeconds(minSecondsBetweenRequests);
                    return;
                }

                wait = target - now;
            }

            Thread.Sleep(wait);
        }
    }

    private static void ApplyCooldown(TimeSpan retryDelay)
    {
        lock (Sync)
        {
            var cooldownUntil = DateTime.UtcNow.Add(retryDelay);
            if (cooldownUntil > blockedUntilUtc)
                blockedUntilUtc = cooldownUntil;
            if (nextRequestUtc < blockedUntilUtc)
                nextRequestUtc = blockedUntilUtc.AddSeconds(minSecondsBetweenRequests);
        }
    }

    private static bool TryGetRateLimitDelay(WebException exception, out TimeSpan retryDelay)
    {
        retryDelay = TimeSpan.FromSeconds(rateLimitCooldownSeconds);
        if (exception.Response is not HttpWebResponse response)
            return false;

        var statusCode = (int)response.StatusCode;
        if (statusCode != 403 && statusCode != 429)
            return false;

        if (double.TryParse(response.Headers["Retry-After"], NumberStyles.Float, CultureInfo.InvariantCulture, out var headerDelay))
            retryDelay = TimeSpan.FromSeconds(Math.Max(1d, headerDelay));

        try
        {
            using var stream = response.GetResponseStream();
            if (stream == null)
                return true;
            using var reader = new StreamReader(stream);
            var body = reader.ReadToEnd();
            if (statusCode == 403 && body.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }
        catch
        {
            return true;
        }

        return true;
    }
}

public sealed class TimeoutWebClient : WebClient
{
    private const int TimeoutMilliseconds = 15000;

    protected override WebRequest GetWebRequest(Uri address)
    {
        var request = base.GetWebRequest(address);
        if (request == null)
            return null;

        request.Timeout = TimeoutMilliseconds;
        if (request is HttpWebRequest httpRequest)
            httpRequest.ReadWriteTimeout = TimeoutMilliseconds;
        return request;
    }
}

public sealed class RedditRateLimitException : Exception
{
    public RedditRateLimitException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
