using Microsoft.Extensions.DependencyInjection;
using Hubbly.Domain.Events;

namespace Hubbly.Application.Services;

public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public DomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            var handleMethod = handlerType.GetMethod("HandleAsync");
            if (handleMethod != null)
            {
                var task = (Task)handleMethod.Invoke(handler, new object?[] { @event, cancellationToken })!;
                await task.ConfigureAwait(false);
            }
        }
    }

    public async Task DispatchAllAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var @event in events)
        {
            await DispatchAsync(@event, cancellationToken);
        }
    }
}
