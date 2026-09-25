using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Listings;

namespace Borea.Network.GitHub;

/// <summary>
/// The REST API of GitHub, through the signed-in session or without the token. It follows redirects only inside https://api.github.com.
/// It never commits to or moves the base branch of a KSAModding repository directly, because the token of a steward bypasses the ruleset of that branch.
/// </summary>
internal sealed class GitHubApi
{
    internal const string Root = "https://api.github.com";

    internal const string Organization = "KSAModding";

    internal const int PageSize = 100;

    internal static readonly TimeSpan SecondaryLimitWait = TimeSpan.FromMinutes(1);

    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private const int MaxRedirects = 5;

    private const int MaxPages = 10;

    private static readonly string[] CommitBranchPrefixes = ["steward/", "listing-"];

    private readonly IGitHubSession _session;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string[] _protectedBranches;

    /// <param name="http">Sends the requests without the token.</param>
    /// <param name="baseBranch">The base branch of the pull requests, protected like main. Null takes <see cref="ListingPullRequestLinks.Branch"/>.</param>
    public GitHubApi(IGitHubSession session, HttpClient http, TimeProvider? time = null, string? baseBranch = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? TimeProvider.System;
        _protectedBranches = [ListingPullRequestLinks.DefaultBranch, baseBranch ?? ListingPullRequestLinks.Branch];
    }

    /// <summary>
    /// Sends through the session, or without the token when <paramref name="anonymous"/>, and follows a redirect of GitHub,
    /// which the session's client does not follow. Any answer comes back, and only a 401 to the token throws.
    /// </summary>
    /// <exception cref="GitHubApiException">Signed out, not reached, a redirect away from the API, or a write that the branch guard refuses.</exception>
    public async Task<GitHubReply> SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken, bool anonymous = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        var json = body is null ? null : JsonSerializer.Serialize(body);
        var target = new Uri(url);
        for (var redirects = 0; ; redirects++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RefusalOf(method, target, json) is { } refusal)
                throw new GitHubApiException(GitHubApiFailure.ProtectedBranch, refusal);

            using var request = new HttpRequestMessage(method, target);
            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (anonymous)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.Add("X-GitHub-Api-Version", BoreaReleaseCheck.ApiVersion);
            }

            try
            {
                using var response = anonymous
                    ? await _http.SendAsync(request, cancellationToken).ConfigureAwait(false)
                    : await _session.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !anonymous)
                    throw new GitHubApiException(GitHubApiFailure.SignedOut, status: response.StatusCode);

                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location && redirects < MaxRedirects)
                {
                    target = location.IsAbsoluteUri ? location : new Uri(target, location);
                    if (!target.AbsoluteUri.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                        throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, status: response.StatusCode);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new GitHubReply(response.StatusCode, text, MessageOf(text), RetryAtOf(response, text));
            }
            catch (InvalidOperationException exception) when (!anonymous)
            {
                throw new GitHubApiException(GitHubApiFailure.SignedOut, innerException: exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new GitHubApiException(GitHubApiFailure.NetworkError, innerException: exception);
            }
        }
    }

    /// <exception cref="GitHubApiException">The request failed, or GitHub did not answer with a success.</exception>
    public async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken, bool anonymous = false)
    {
        var reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous).ConfigureAwait(false);
        return Parse<T>(Ensure(reply));
    }

    /// <summary>
    /// Reads public data. GitHub can refuse the token on it when the App lacks the permission, for example Commit statuses,
    /// so a refusal that is no rate limit is read again without the token.
    /// </summary>
    /// <exception cref="GitHubApiException">The request failed, or GitHub did not answer with a success.</exception>
    public async Task<T> GetPublicAsync<T>(string url, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
        if (reply is { Status: HttpStatusCode.Forbidden, RetryAt: null })
            reply = await SendAsync(HttpMethod.Get, url, null, cancellationToken, anonymous: true).ConfigureAwait(false);

        return Parse<T>(Ensure(reply));
    }

    /// <summary>The items of a list endpoint that answers with a JSON array, page by page, up to the first page with fewer than <see cref="PageSize"/> items.</summary>
    /// <param name="url">The endpoint with its own query, without per_page and page.</param>
    public IAsyncEnumerable<T> GetPagesAsync<T>(string url, CancellationToken cancellationToken) =>
        GetPagesAsync<List<T>, T>(url, page => page, cancellationToken);

    /// <summary>The same for an endpoint that wraps the array in an object, which <paramref name="items"/> opens.</summary>
    public async IAsyncEnumerable<TItem> GetPagesAsync<TPage, TItem>(string url, Func<TPage, IReadOnlyCollection<TItem>> items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        for (var page = 1; page <= MaxPages; page++)
        {
            var found = items(await GetAsync<TPage>($"{url}{separator}per_page={PageSize}&page={page.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false));
            foreach (var item in found)
                yield return item;

            if (found.Count < PageSize)
                yield break;
        }
    }

    /// <summary>The body of a success.</summary>
    /// <exception cref="GitHubApiException">Any other answer, with GitHub's message and the rate-limit time.</exception>
    public static string Ensure(GitHubReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if ((int)reply.Status is >= 200 and < 300)
            return reply.Body;

        var failure = reply.Status switch
        {
            _ when reply.RetryAt is not null => GitHubApiFailure.RateLimited,
            HttpStatusCode.Forbidden => GitHubApiFailure.Forbidden,
            HttpStatusCode.NotFound => GitHubApiFailure.NotFound,
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict => GitHubApiFailure.Refused,
            _ => GitHubApiFailure.UnexpectedResponse,
        };
        var detail = failure == GitHubApiFailure.UnexpectedResponse ? $"HTTP {(int)reply.Status}" : reply.Message;
        throw new GitHubApiException(failure, detail, reply.RetryAt, reply.Status);
    }

    /// <exception cref="GitHubApiException">The body is not <typeparamref name="T"/>.</exception>
    public static T Parse<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json) ?? throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);
        }
        catch (JsonException exception)
        {
            throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse, innerException: exception);
        }
    }

    /// <summary>
    /// Why the guard refuses a write to a repository of the organization, or null. A commit goes only to a steward/ or listing- branch that is not protected,
    /// and a ref, merge or branch change never touches a protected branch. A repository addressed by its id counts as one of the organization.
    /// </summary>
    private string? RefusalOf(HttpMethod method, Uri target, string? json)
    {
        if (method == HttpMethod.Get || method == HttpMethod.Head)
            return null;

        var segments = target.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        string[] path;
        if (segments is ["repos", var owner, _, ..])
        {
            if (!string.Equals(owner, Organization, StringComparison.OrdinalIgnoreCase))
                return null;
            path = segments[3..];
        }
        else if (segments is ["repositories", _, ..])
        {
            path = segments[2..];
        }
        else
        {
            return null;
        }

        return path switch
        {
            ["contents", ..] => CommitRefusal(FieldOf(json, "branch")),
            ["git", "refs"] => Protected(BranchOfRef(FieldOf(json, "ref"))),
            ["git", "refs", .. var name] => Protected(BranchOfRef(string.Join('/', name))),
            ["merges"] => Protected(FieldOf(json, "base")),
            ["merge-upstream"] => Protected(FieldOf(json, "branch")),
            ["branches", ..] => _protectedBranches.FirstOrDefault(branch => IsBranchPath(path[1..], branch)) is { } named ? Refused(named) : null,
            _ => null,
        };
    }

    private string? CommitRefusal(string? branch) =>
        Protected(branch) ?? (branch is not null && CommitBranchPrefixes.Any(prefix => branch.StartsWith(prefix, StringComparison.Ordinal))
            ? null
            : $"Borea commits only to a branch that starts with {string.Join(" or ", CommitBranchPrefixes)}, not to {branch ?? "the default branch"}");

    private string? Protected(string? branch) =>
        branch is not null && _protectedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase) ? Refused(branch) : null;

    private static string Refused(string branch) => $"Borea does not write to {branch}";

    private static string? BranchOfRef(string? reference)
    {
        if (reference is null)
            return null;

        var name = reference.StartsWith("refs/", StringComparison.Ordinal) ? reference["refs/".Length..] : reference;
        return name.StartsWith("heads/", StringComparison.Ordinal) ? name["heads/".Length..] : null;
    }

    /// <summary>
    /// Whether a path under branches/ is <paramref name="branch"/> or one of its settings, for a branch name with slashes too.
    /// The segments are joined again, because a slash of the name can come escaped as %2F inside one segment.
    /// </summary>
    private static bool IsBranchPath(string[] rest, string branch)
    {
        var joined = string.Join('/', rest);
        return joined.Equals(branch, StringComparison.OrdinalIgnoreCase) || joined.StartsWith(branch + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FieldOf(string? json, string name)
    {
        if (json is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private DateTimeOffset? RetryAtOf(HttpResponseMessage response, string body)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return null;

        var now = _time.GetUtcNow();
        if (response.Headers.RetryAfter is { } retryAfter)
            return retryAfter.Delta is { } delta ? now + delta : retryAfter.Date ?? now + SecondaryLimitWait;

        if (Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset))
        {
            return DateTimeOffset.FromUnixTimeSeconds(reset);
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            ? now + SecondaryLimitWait
            : null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? MessageOf(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorDto>(body, Json);
            var details = error?.Errors?.Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String => message.GetString(),
                _ => null,
            }) ?? [];
            var parts = new[] { error?.Message }.Concat(details).Where(part => !string.IsNullOrWhiteSpace(part));
            var message = string.Join(". ", parts);
            return message.Length > 0 ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ErrorDto
    {
        public string? Message { get; set; }

        public List<JsonElement>? Errors { get; set; }
    }
}

/// <param name="Message">GitHub's error message with its details, or null.</param>
/// <param name="RetryAt">When GitHub accepts requests again after a rate limit, or null.</param>
internal sealed record GitHubReply(HttpStatusCode Status, string Body, string? Message, DateTimeOffset? RetryAt);

internal enum GitHubApiFailure
{
    SignedOut,
    RateLimited,
    NotFound,
    Refused,
    Forbidden,
    NetworkError,
    UnexpectedResponse,

    /// <summary>The branch guard refused a write before anything was sent.</summary>
    ProtectedBranch,
}

/// <summary>A request to the GitHub API failed. The message never holds the token.</summary>
internal sealed class GitHubApiException(GitHubApiFailure failure, string? detail = null, DateTimeOffset? retryAt = null, HttpStatusCode? status = null, Exception? innerException = null)
    : Exception(failure + (detail is null ? string.Empty : ", " + detail), innerException)
{
    public GitHubApiFailure Failure { get; } = failure;

    /// <summary>What GitHub or the branch guard said.</summary>
    public string? Detail { get; } = detail;

    public DateTimeOffset? RetryAt { get; } = retryAt;

    /// <summary>The status of GitHub's answer, when there was one.</summary>
    public HttpStatusCode? Status { get; } = status;
}
