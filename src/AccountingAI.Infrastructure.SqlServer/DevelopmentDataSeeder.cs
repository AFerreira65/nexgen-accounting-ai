using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccountingAI.Infrastructure.SqlServer;

public static class DevelopmentDataSeeder
{
    public static readonly Guid DemoFirmId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DemoUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid DemoIslandCompanyId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task SeedDevelopmentOrganizationAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();

        if (!await context.AccountingFirms
                .AnyAsync(item => item.Id == DemoFirmId, cancellationToken)
                .ConfigureAwait(false))
        {
            context.Add(new AccountingFirm(DemoFirmId, "Gabinete Horizonte", "gabinete-horizonte"));
        }

        var demoCompany = await context.Companies
            .SingleOrDefaultAsync(item => item.Id == DemoCompanyId, cancellationToken)
            .ConfigureAwait(false);
        if (demoCompany is null)
        {
            demoCompany = new Company(
                DemoCompanyId,
                DemoFirmId,
                "Almeida & Costa, Lda. (Demonstração)",
                "DEMO-PT-0001",
                TaxRegion.Mainland,
                accountingStandard: AccountingStandard.SncGeneral);
            context.Add(demoCompany);
        }
        demoCompany.ChangeAccountingStandard(AccountingStandard.SncGeneral);

        if (!await context.UserAccounts
                .AnyAsync(item => item.Id == DemoUserId, cancellationToken)
                .ConfigureAwait(false))
        {
            context.Add(new UserAccount(
                DemoUserId,
                "António Ferreira (Demonstração)",
                "antonio.demo@accountingai.local"));
        }

        if (!await context.CompanyUsers
                .AnyAsync(
                    item => item.CompanyId == DemoCompanyId && item.UserId == DemoUserId,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            context.Add(new CompanyUser(DemoCompanyId, DemoUserId, CompanyUserRole.Owner));
        }

        if (!await context.UserCompanySettings
                .AnyAsync(
                    item => item.CompanyId == DemoCompanyId && item.UserId == DemoUserId,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            var settings = new UserCompanySettings(DemoCompanyId, DemoUserId);
            settings.Update("pt-PT", "Europe/Lisbon", 2026, InterfaceTheme.Light, false, 50, InterfaceTextSize.Normal);
            context.Add(settings);
        }

        var demoIslandCompany = await context.Companies
            .SingleOrDefaultAsync(item => item.Id == DemoIslandCompanyId, cancellationToken)
            .ConfigureAwait(false);
        if (demoIslandCompany is null)
        {
            demoIslandCompany = new Company(
                DemoIslandCompanyId,
                DemoFirmId,
                "Ilha Azul Serviços, Lda. (Demonstração)",
                "DEMO-PT-0002",
                TaxRegion.Azores,
                accountingStandard: AccountingStandard.SncMicroEntities);
            context.Add(demoIslandCompany);
        }
        demoIslandCompany.ChangeAccountingStandard(AccountingStandard.SncMicroEntities);

        if (!await context.CompanyUsers
                .AnyAsync(
                    item => item.CompanyId == DemoIslandCompanyId && item.UserId == DemoUserId,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            context.Add(new CompanyUser(DemoIslandCompanyId, DemoUserId, CompanyUserRole.Accountant));
        }

        if (!await context.UserCompanySettings
                .AnyAsync(
                    item => item.CompanyId == DemoIslandCompanyId && item.UserId == DemoUserId,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            var settings = new UserCompanySettings(DemoIslandCompanyId, DemoUserId);
            settings.Update("pt-PT", "Atlantic/Azores", 2026, InterfaceTheme.System, true, 25, InterfaceTextSize.Large);
            context.Add(settings);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
