using Microsoft.Extensions.Caching.Memory;

namespace Hubbly.Api.Services;

/// <summary>
/// Validates chat messages and handles nonce validation for anti-replay protection.
/// Extracted from ChatHub to follow Single Responsibility Principle.
/// </summary>
public class MessageValidationService : IMessageValidationService
{
    private readonly IMemoryCache _nonceCache;
    private readonly ILogger<MessageValidationService> _logger;

    private static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(2);

    public MessageValidationService(
        IMemoryCache nonceCache,
        ILogger<MessageValidationService> logger)
    {
        _nonceCache = nonceCache ?? throw new ArgumentNullException(nameof(nonceCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates the message request parameters.
    /// </summary>
    /// <param name="content">Message content</param>
    /// <param name="timestamp">Client timestamp</param>
    /// <param name="nonce">Unique nonce for this message</param>
    /// <returns>
    /// Validation result with success flag and error message if failed
    /// </returns>
    public (bool isValid, string? errorMessage) ValidateMessage(
        string? content,
        long? timestamp,
        string? nonce)
    {
        // Validate timestamp
        if (!timestamp.HasValue)
        {
            _logger.LogWarning("Missing timestamp in message");
            return (false, "Missing timestamp");
        }

        // Validate nonce
        if (string.IsNullOrEmpty(nonce) || !IsNonceValid(nonce, timestamp.Value))
        {
            _logger.LogWarning("Invalid message token (nonce validation failed)");
            return (false, "Invalid message token");
        }

        // Validate content
        if (string.IsNullOrEmpty(content))
        {
            _logger.LogWarning("Empty message content");
            return (false, "Message cannot be empty");
        }

        if (content.Length > 500)
        {
            _logger.LogWarning("Message too long: {Length}", content.Length);
            return (false, "Message too long");
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if a nonce is valid (not reused and within time window).
    /// </summary>
    public bool IsNonceValid(string nonce, long clientTimestamp)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeDiff = Math.Abs(now - clientTimestamp);

        if (timeDiff > 300) // 5 minutes to account for clock drift
        {
            _logger.LogWarning("Nonce rejected: time diff {TimeDiff}s", timeDiff);
            return false;
        }

        var cacheKey = $"nonce_{nonce}";
        if (_nonceCache.TryGetValue(cacheKey, out _))
        {
            _logger.LogWarning("Nonce rejected: already used");
            return false;
        }

        _nonceCache.Set(cacheKey, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = NonceLifetime,
            Size = 1
        });
        return true;
    }
}
