using NexGenAccountingAI.Application.ManagementReports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.Ai;

public static class DependencyInjection
{
    /// <summary>
    /// Regista o narrador dos relatórios de gestão. Liga <see cref="AiOptions"/>
    /// da secção <c>Ai</c> e completa a chave com a variável de ambiente
    /// <c>ANTHROPIC_API_KEY</c> quando a secção não a traz. Sem chave, o narrador
    /// resolvido comporta-se como o nulo (<see cref="IManagementReportNarrator.IsConfigured"/>
    /// fica <c>false</c>).
    /// </summary>
    public static IServiceCollection AddAccountingAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.PostConfigure<AiOptions>(options =>
            options.ApiKey = string.IsNullOrWhiteSpace(options.ApiKey)
                ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                : options.ApiKey);

        services.AddSingleton<IManagementReportNarrator, ClaudeManagementReportNarrator>();
        return services;
    }
}
