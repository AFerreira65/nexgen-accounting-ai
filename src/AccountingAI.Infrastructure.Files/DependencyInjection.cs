using AccountingAI.Application.Saft;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.Infrastructure.Files;

public static class DependencyInjection
{
    public static IServiceCollection AddSaftFileReading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISaftHeaderReader, SaftHeaderReader>();
        services.AddSingleton<ISaftAccountsReader, SaftAccountsReader>();
        services.AddSingleton<ISaftEntriesReader, SaftEntriesReader>();

        return services;
    }
}
