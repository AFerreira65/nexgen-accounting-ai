using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O utilizador autenticado, sem nada sensível.</summary>
/// <param name="IsProducer">A conta do produtor: sem empresas, só vê a configuração geral da aplicação (ADR-0017).</param>
public sealed record AuthenticatedUser(Guid Id, string DisplayName, string Email, bool IsProducer = false);

/// <summary>
/// Valida credenciais e define senhas de <see cref="UserAccount"/>. O hash usa
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2, o mesmo do ASP.NET Core
/// Identity) sem trazer o resto do <em>framework</em> Identity — ver
/// ADR-0011. Não existe endpoint de registo: só a ferramenta de linha de
/// comandos <c>set-user-password</c> define uma senha.
/// </summary>
public sealed class AuthService(AccountingDbContext context)
{
    private readonly PasswordHasher<UserAccount> _hasher = new();

    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await context.UserAccounts
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail && item.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (user?.PasswordHash is null)
        {
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(_hasher.HashPassword(user, password));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AuthenticatedUser(user.Id, user.DisplayName, user.Email, user.IsProducer);
    }

    /// <summary>
    /// Cria a conta do produtor — sem empresas, só com a configuração geral da aplicação —
    /// ou, se o email já for desta conta, só a mantém (a senha define-se com <see cref="SetPasswordAsync"/>).
    /// Só a ferramenta de linha de comandos a chama (ADR-0017).
    /// </summary>
    public async Task<AuthenticatedUser> CreateProducerAsync(string email, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await context.UserAccounts
            .Include(item => item.CompanyMemberships)
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = new UserAccount(Guid.NewGuid(), displayName, email);
            user.MarkAsProducer();
            context.UserAccounts.Add(user);
        }
        else if (!user.IsProducer)
        {
            throw new InvalidOperationException("Já existe um utilizador normal com esse email: o produtor tem de ter uma conta própria, sem empresas.");
        }
        else
        {
            user.Rename(displayName);
            user.Reactivate();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AuthenticatedUser(user.Id, user.DisplayName, user.Email, true);
    }

    /// <summary>Retira a conta do produtor: fica desativada e deixa de o ser.</summary>
    public async Task RemoveProducerAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await context.UserAccounts
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail && item.IsProducer, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Não há um produtor com o email '{email}'.");
        user.ClearProducer();
        user.Deactivate();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Se a conta de sessão é o produtor (ativo). Verifica-se na base a cada pedido.</summary>
    public Task<bool> IsProducerAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.UserAccounts
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.IsActive && item.IsProducer, cancellationToken);

    public async Task SetPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var user = await context.UserAccounts
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            throw new InvalidOperationException($"Utilizador '{email}' não encontrado.");
        }

        user.SetPasswordHash(_hasher.HashPassword(user, password));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
