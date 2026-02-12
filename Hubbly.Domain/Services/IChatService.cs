using Hubbly.Domain.Dtos;

namespace Hubbly.Domain.Services;

public interface IChatService
{
    Task<ChatMessageDto> SendMessageAsync(Guid senderId, string content, string? actionType = null);
    Task<bool> IsMessageValidAsync(string content); // Антимат + валидация
}
