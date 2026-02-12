using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;

namespace Hubbly.Application.Services;

public class ChatService : IChatService
{
    private readonly IUserRepository _userRepository;
    private readonly IProfanityFilterService _profanityFilterService;

    public ChatService(
        IUserRepository userRepository,
        IProfanityFilterService profanityFilterService)
    {
        _userRepository = userRepository;
        _profanityFilterService = profanityFilterService;
    }

    public async Task<ChatMessageDto> SendMessageAsync(Guid senderId, string content, string? actionType = null)
    {
        // 1. Проверяем, существует ли пользователь
        var sender = await _userRepository.GetByIdAsync(senderId);
        if (sender == null)
        {
            throw new KeyNotFoundException("Sender not found.");
        }

        // 2. Валидируем сообщение (антимат, длина и т.д.)
        if (!await IsMessageValidAsync(content))
        {
            throw new InvalidOperationException("Message contains invalid content.");
        }

        // 3. ВАЖНО: Валидируем actionType (если передан)
        if (!string.IsNullOrEmpty(actionType) && !IsValidActionType(actionType))
        {
            throw new InvalidOperationException($"Invalid action type: {actionType}");
        }
        
        // 4. Возвращаем DTO
        return new ChatMessageDto
        {
            Id = Guid.NewGuid(), // Временный ID
            SenderId = senderId,
            SenderNickname = sender.Nickname,
            Content = content,
            SentAt = DateTimeOffset.UtcNow,
            ActionType = actionType
        };
    }
    
    public async Task<bool> IsMessageValidAsync(string content)
    {
        // 1. Проверка длины
        if (string.IsNullOrWhiteSpace(content) || content.Length > 500)
        {
            return false;
        }

        // 2. Антимат-фильтр
        if (await _profanityFilterService.ContainsProfanityAsync(content))
        {
            return false;
        }

        // 3. Проверка на спам (например, повторяющиеся символы)
        if (IsSpam(content))
        {
            return false;
        }

        return true;
    }

    private bool IsSpam(string content)
    {
        // Пример: блокируем сообщения с более чем 5 повторяющимися символами подряд
        return System.Text.RegularExpressions.Regex.IsMatch(content, @"(.)\1{5,}");
    }

    private bool IsValidActionType(string actionType)
    {
        var validActions = new[] { "wave", "laugh", "applause" };
        return validActions.Contains(actionType.ToLower());
    }
}
