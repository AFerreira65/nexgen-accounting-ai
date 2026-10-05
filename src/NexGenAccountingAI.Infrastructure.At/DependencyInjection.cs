using NexGenAccountingAI.Application.At;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.At;

public static class DependencyInjection
{
    /// <summary>
    /// Regista o <see cref="IAtCertificateProvider"/>. Liga <see cref="AtOptions"/>
    /// da secção <c>At</c> e completa a senha do certificado com a variável de
    /// ambiente <c>AT_CERTIFICATE_PASSWORD</c> quando a secção não a traz.
    /// </summary>
    public static IServiceCollection AddAccountingAt(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AtOptions>(configuration.GetSection(AtOptions.SectionName));
        services.PostConfigure<AtOptions>(options =>
            options.Certificate.Password = string.IsNullOrWhiteSpace(options.Certificate.Password)
                ? Environment.GetEnvironmentVariable("AT_CERTIFICATE_PASSWORD")
                : options.Certificate.Password);

        var keyRingPath = configuration.GetSection(AtOptions.SectionName)["KeyRingPath"];
        services.AddSingleton(_ => AtSecretProtector.Create(keyRingPath));
        services.AddSingleton<IAtSecretProtector>(sp => sp.GetRequiredService<AtSecretProtector>());
        services.AddSingleton<AtCertificateProvider>();
        services.AddSingleton<IAtCertificateProvider>(sp => sp.GetRequiredService<AtCertificateProvider>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IVatReturnAtGateway, VatReturnAtGateway>();
        return services;
    }
}
