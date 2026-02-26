namespace Hubbly.Api.Services;

/// <summary>
/// Validates chat messages and handles nonce validation for anti-replay protection.
/// </summary>
public interface IMessageValidationService
{
    /// <summary>
    /// Validates the message request parameters.
    /// </summary>
    /// <returns>
    /// Validation result with success flag and error message if failed
    /// </returns>
    (bool isValid, string? errorMessage) ValidateMessage(
        string? content,
        long? timestamp,
        string? nonce);

    /// <summary>
    /// Checks if a nonce is valid (not reused and within time window).
    /// </summary>
    bool IsNonceValid(string nonce, long clientTimestamp);
}
