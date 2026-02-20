using System.Reflection;

namespace Hubbly.Domain.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default);
    Task DispatchAllAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
