using Hubbly.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hubbly.Application.Services;

public class RoomCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RoomCleanupService> _logger;
    private readonly TimeSpan _emptyRoomTTL = TimeSpan.FromDays(10);

    public RoomCleanupService(IServiceProvider serviceProvider, ILogger<RoomCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[CLEANUP] Room cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var roomService = scope.ServiceProvider.GetRequiredService<IRoomService>();

                // Очищаем раз в час, но удаляем через 1 день (было 10 дней)
                await roomService.CleanupEmptyRoomsAsync(TimeSpan.FromDays(1));

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CLEANUP] Room cleanup failed");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
