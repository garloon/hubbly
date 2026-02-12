using Hubbly.Domain.Services;
using System.Text.RegularExpressions;

namespace Hubbly.Application.Services;

public class ProfanityFilterService : IProfanityFilterService
{
    // Список запрещенных слов (можно загружать из БД или файла)
    private readonly HashSet<string> _bannedWords = new()
{
    "badword1", "badword2", "spam", "scam"
};

    public Task<bool> ContainsProfanityAsync(string text)
    {
        var normalizedText = text.ToLowerInvariant();
        foreach (var word in _bannedWords)
        {
            if (normalizedText.Contains(word))
            {
                return Task.FromResult(true);
            }
        }

        // Дополнительная проверка на маскировку символов (например, "b@dword")
        var hasMaskedProfanity = _bannedWords.Any(word =>
            Regex.IsMatch(normalizedText, $"[{word[0]}@#$%]{word[1..]}", RegexOptions.IgnoreCase));

        return Task.FromResult(hasMaskedProfanity);
    }
}
