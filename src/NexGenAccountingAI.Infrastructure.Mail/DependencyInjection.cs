using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Mail;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.Mail;

public static class DependencyInjection
{
    /// <summary>Regista o envio por SMTP, a leitura por IMAP e o cliente OAuth2 (Microsoft e Google).</summary>
    public static IServiceCollection AddAccountingMail(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMailSender, MailKitSender>();
        services.AddSingleton<IMailReader, MailKitReader>();
        services.AddHttpClient<IMailOAuthClient, MailOAuthClient>();
        return services;
    }
}
