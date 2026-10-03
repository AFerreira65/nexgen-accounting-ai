using AccountingAI.Application.Efatura;
using AccountingAI.Application.Saft;
using AccountingAI.Application.Saft.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.Infrastructure.Files;

public static class DependencyInjection
{
    public static IServiceCollection AddSaftFileReading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISaftHeaderReader, SaftHeaderReader>();
        services.AddSingleton<ISaftAccountsReader, SaftAccountsReader>();
        services.AddSingleton<ISaftPartiesReader, SaftPartiesReader>();
        services.AddSingleton<ISaftEntriesReader, SaftEntriesReader>();
        services.AddSingleton<ISaftSalesInvoicesReader, SaftSalesInvoicesReader>();
        services.AddSingleton<ISaftSchemaValidator, SaftSchemaValidator>();
        services.AddSingleton<ISaftValidationModelReader, SaftValidationModelReader>();
        services.AddSingleton<IEFaturaCsvReader, EFaturaCsvReader>();

        services.AddSingleton<ISaftAnalyticalRule, NifCheckDigitRule>();
        services.AddSingleton<ISaftAnalyticalRule, AccountingBasisRule>();
        services.AddSingleton<ISaftAnalyticalRule, PeriodRangeRule>();
        services.AddSingleton<ISaftAnalyticalRule, ProductIdentificationRule>();
        services.AddSingleton<ISaftAnalyticalRule, EntryCountRule>();
        services.AddSingleton<ISaftAnalyticalRule, FileTotalsRule>();
        services.AddSingleton<ISaftAnalyticalRule, TransactionBalancedRule>();
        services.AddSingleton<ISaftAnalyticalRule, AccountReferenceRule>();
        services.AddSingleton<ISaftAnalyticalRule, EntryDatesInPeriodRule>();
        services.AddSingleton<ISaftAnalyticalRule, AccountingHasJournalsRule>();
        services.AddSingleton<ISaftAnalyticalValidator, SaftAnalyticalValidator>();

        return services;
    }
}
