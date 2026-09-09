using AccountingAI.Application.At;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.Infrastructure.At;

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

        services.AddSingleton<IAtCertificateProvider, AtCertificateProvider>();
        return services;
    }
}
