using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Events;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Hubbly.Application.Services;

public class ChatService : IChatService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<ChatService> _logger;
    private readonly IDomainEventDispatcher _eventDispatcher;

    private static readonly HashSet<string> ValidActionTypes = new()
    {
        "clap", "wave", "dance", "laugh", "applause"
    };

    public ChatService(
        IUserRepository userRepository,
        ILogger<ChatService> logger,
        IDomainEventDispatcher eventDispatcher)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
    }

    #region Public methods

    public async Task<ChatMessageDto> SendMessageAsync(Guid senderId, string content, string? actionType = null)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["SenderId"] = senderId,
            ["ContentLength"] = content?.Length ?? 0,
            ["ActionType"] = actionType ?? "none"
        }))
        {
            _logger.LogDebug("SendMessageAsync started");

            try
            {
                // 1. Check user
                var sender = await GetSenderAsync(senderId);

                if (string.IsNullOrWhiteSpace(content))
                    throw new ArgumentException("Message content cannot be empty");

                // 2. Validate message
                await ValidateMessageAsync(content, actionType);

                // 3. Create DTO
                var messageDto = CreateMessageDto(senderId, sender.Nickname, content, actionType);

                _logger.LogInformation("Message sent by {SenderNickname}", sender.Nickname);

                // Dispatch MessageSentEvent
                var messageSentEvent = new MessageSentEvent(senderId, messageDto);
                await _eventDispatcher.DispatchAsync(messageSentEvent);

                return messageDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendMessageAsync failed for user {SenderId}", senderId);
                throw;
            }
        }
    }

    public async Task<bool> IsMessageValidAsync(string content)
    {
        try
        {
            return await ValidateMessageContentAsync(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating message");
            return false;
        }
    }

    #endregion

    #region Private methods

    private async Task<User> GetSenderAsync(Guid senderId)
    {
        var sender = await _userRepository.GetByIdAsync(senderId);
        if (sender == null)
        {
            _logger.LogWarning("Sender not found: {SenderId}", senderId);
            throw new KeyNotFoundException($"Sender with id {senderId} not found.");
        }
        return sender;
    }

    private async Task ValidateMessageAsync(string content, string? actionType)
    {
        // Content validation
        if (!await ValidateMessageContentAsync(content))
        {
            _logger.LogWarning("Message contains invalid content");
            throw new InvalidOperationException("Message contains invalid content.");
        }

        // Action type validation
        if (!string.IsNullOrEmpty(actionType) && !IsValidActionType(actionType))
        {
            _logger.LogWarning("Invalid action type: {ActionType}", actionType);
            throw new InvalidOperationException($"Invalid action type: {actionType}");
        }
    }

    private async Task<bool> ValidateMessageContentAsync(string content)
    {
        // Length check
        if (string.IsNullOrWhiteSpace(content) || content.Length > 500)
        {
            _logger.LogDebug("Message validation failed: length {Length}", content?.Length ?? 0);
            return false;
        }
        
        // Spam check
        if (IsSpam(content))
        {
            _logger.LogDebug("Message validation failed: spam detected");
            return false;
        }

        return true;
    }

    private bool IsSpam(string content)
    {
        // Block messages with more than 5 consecutive repeating characters
        return System.Text.RegularExpressions.Regex.IsMatch(content, @"(.)\1{5,}");
    }

    private bool IsValidActionType(string actionType)
    {
        return ValidActionTypes.Contains(actionType.ToLowerInvariant());
    }

    private ChatMessageDto CreateMessageDto(Guid senderId, string senderNickname, string content, string? actionType)
    {
        return new ChatMessageDto
        {
            Id = Guid.NewGuid(),
            SenderId = senderId,
            SenderNickname = senderNickname,
            Content = content,
            SentAt = DateTimeOffset.UtcNow,
            ActionType = actionType
        };
    }

    #endregion
}