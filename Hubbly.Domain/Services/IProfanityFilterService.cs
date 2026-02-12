namespace Hubbly.Domain.Services;

public interface IProfanityFilterService
{
    Task<bool> ContainsProfanityAsync(string text);
}
