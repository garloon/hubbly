namespace Hubbly.Domain.Entities;

public class ChatRoom
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public int CurrentUsers { get; private set; }
    public int MaxUsers { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastActiveAt { get; private set; }
    public bool IsMarkedForDeletion { get; private set; }

    private ChatRoom() { }

    public ChatRoom(string name, int maxUsers = 50)
    {
        Id = Guid.NewGuid();
        Name = name;
        CurrentUsers = 0;
        MaxUsers = maxUsers;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActiveAt = DateTimeOffset.UtcNow;
        IsMarkedForDeletion = false;
    }

    public void UserJoined()
    {
        // ЗАЩИТА: нельзя превысить лимит комнаты
        if (CurrentUsers >= MaxUsers)
        {
            throw new InvalidOperationException($"Room is full (max {MaxUsers})");
        }

        CurrentUsers++;
        LastActiveAt = DateTimeOffset.UtcNow;
        IsMarkedForDeletion = false;
    }

    public void UserLeft()
    {
        if (CurrentUsers > 0)
            CurrentUsers--;

        if (CurrentUsers == 0)
            LastActiveAt = DateTimeOffset.UtcNow;
    }

    public bool IsActive => CurrentUsers < MaxUsers;
    public bool IsEmpty => CurrentUsers == 0;
}
