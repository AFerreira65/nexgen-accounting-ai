using NexGenAccountingAI.Application.Banking;
using NexGenAccountingAI.Application.Efatura;
using NexGenAccountingAI.Application.Saft;
using NexGenAccountingAI.Application.Saft.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.Files;

public static class DependencyInjection
{
    public static IServiceCollection AddSaftFileReading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISaftHeaderReader, SaftHeaderReader>();
        services.AddSingleton<ISaftAccountsReader, SaftAccountsReader>();
        services.AddSingleton<ISaftPartiesReader, SaftPartiesReader>();
        services.AddSingleton<ISaftAccountingWriter, SaftAccountingWriter>();
        services.AddSingleton<ISaftEntriesReader, SaftEntriesReader>();
        services.AddSingleton<ISaftSalesInvoicesReader, SaftSalesInvoicesReader>();
        services.AddSingleton<ISaftSalesDocumentsReader, SaftSalesDocumentsReader>();
        services.AddSingleton<ISaftSchemaValidator, SaftSchemaValidator>();
        services.AddSingleton<ISaftValidationModelReader, SaftValidationModelReader>();
        services.AddSingleton<IEFaturaCsvReader, EFaturaCsvReader>();
        services.AddSingleton<IBankStatementReader, BankStatementReader>();

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
