namespace Hubbly.Domain.Events;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
