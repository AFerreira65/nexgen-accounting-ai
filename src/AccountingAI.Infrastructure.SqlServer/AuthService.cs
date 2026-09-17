using AccountingAI.Domain.Organizations;
using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AccountingAI.Infrastructure.SqlServer;

/// <summary>O utilizador autenticado, sem nada sensível.</summary>
public sealed record AuthenticatedUser(Guid Id, string DisplayName, string Email);

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

        return new AuthenticatedUser(user.Id, user.DisplayName, user.Email);
    }

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
