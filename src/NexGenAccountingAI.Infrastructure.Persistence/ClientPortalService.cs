using System.Security.Cryptography;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Archive;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Domain.Portal;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

public sealed record PortalUserView(Guid AccountId, string Email, string DisplayName, string Status, DateTimeOffset? LastLoginAtUtc, DateTimeOffset? InviteExpiresAtUtc);

/// <summary>O resultado de um convite: se o email seguiu e, se não, o endereço para o gabinete o enviar à mão.</summary>
public sealed record PortalInviteResult(PortalUserView User, bool EmailSent, string? Link, string? EmailError);

public sealed record PortalItemView(
    Guid Id, string Category, string Title, string FileName, string ContentType, long SizeBytes, DateTimeOffset PublishedAtUtc, bool FromArchive, string? PublishedByName);

public sealed record PortalInvitationInfo(string Email, string DisplayName, IReadOnlyList<string> Companies, bool IsReset = false);

/// <summary>Um documento enviado por um cliente pelo portal; <c>SenderName</c> só aparece ao gabinete.</summary>
public sealed record PortalUploadView(Guid Id, string FileName, long SizeBytes, DateTimeOffset UploadedAtUtc, string? SenderName);

public sealed record PortalSession(Guid AccountId, string DisplayName, string Email);

public sealed record PortalLoginResult(PortalSession? Session, string? Error);

public sealed record PortalCompanyView(Guid CompanyId, string Name);

/// <summary>
/// O portal do cliente (ADR-0020). O gabinete convida um cliente por email; o cliente confirma a senha pelo link e a conta
/// fica ativa. Vê só o que o gabinete publicou — documentos do arquivo, relatórios, balancetes e mapas de gestão — e só das
/// empresas a que a conta tem acesso. A conta é à parte dos utilizadores do gabinete e não chega a nenhum endpoint da aplicação.
/// </summary>
public sealed class ClientPortalService(AccountingDbContext context, MailAccountService mail, DocumentArchiveService archive, TimeProvider time)
{
    public const int MinPasswordLength = 10;

    /// <summary>Quantos documentos uma conta pode enviar por dia.</summary>
    public const int MaxUploadsPerDay = 50;

    private readonly PasswordHasher<ClientPortalAccount> hasher = new();

    // ---- O gabinete ------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PortalUserView>> ListUsersAsync(Guid actingUserId, Guid companyId, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var rows = await context.ClientPortalAccess.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => item.Account)
            .OrderBy(account => account.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(View).ToList();
    }

    /// <summary>Convida um cliente para o portal da empresa. Se já tem conta ativa, só ganha o acesso.</summary>
    public async Task<PortalInviteResult> InviteAsync(
        Guid actingUserId, Guid companyId, string email, string displayName, string linkBase, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToUpperInvariant();
        var account = await context.ClientPortalAccounts.Include(item => item.Companies)
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            account = new ClientPortalAccount(Guid.NewGuid(), email, string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName);
            context.ClientPortalAccounts.Add(account);
        }
        else if (!string.IsNullOrWhiteSpace(displayName) && account.PasswordHash is null)
        {
            account.SetName(displayName);
        }

        if (!account.Companies.Any(item => item.CompanyId == companyId))
        {
            context.ClientPortalAccess.Add(new ClientPortalAccess(account.Id, companyId, actingUserId));
        }

        if (account.Status == PortalAccountStatus.Disabled)
        {
            account.Enable();
        }

        var company = await context.Companies.AsNoTracking().Where(item => item.Id == companyId).Select(item => new { item.LegalName, item.AccountingFirmId }).SingleAsync(cancellationToken).ConfigureAwait(false);
        string? link = null;
        string subject;
        string body;
        if (account.PasswordHash is not null && account.Status == PortalAccountStatus.Active)
        {
            subject = $"Acesso ao portal do cliente — {company.LegalName}";
            body = $"Olá, {account.DisplayName}.{Environment.NewLine}{Environment.NewLine}Passou a poder ver no portal do cliente os documentos e relatórios de {company.LegalName}.{Environment.NewLine}{Environment.NewLine}Entre em {linkBase}/cliente com o seu email e a sua senha.";
        }
        else
        {
            link = NewInvitation(account, linkBase);
            (subject, body) = InvitationMessage(account, company.LegalName, link);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await SendAsync(account, company.AccountingFirmId, subject, body, link, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Envia outro convite (ou, a uma conta já ativa, um link para escolher uma senha nova).</summary>
    public async Task<PortalInviteResult> ResendAsync(Guid actingUserId, Guid companyId, Guid accountId, string linkBase, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var account = await context.ClientPortalAccounts
            .SingleOrDefaultAsync(item => item.Id == accountId && item.Companies.Any(access => access.CompanyId == companyId), cancellationToken)
            .ConfigureAwait(false) ?? throw new KeyNotFoundException("O utilizador do portal não existe.");
        var company = await context.Companies.AsNoTracking().Where(item => item.Id == companyId).Select(item => new { item.LegalName, item.AccountingFirmId }).SingleAsync(cancellationToken).ConfigureAwait(false);
        var link = NewInvitation(account, linkBase);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var (subject, body) = InvitationMessage(account, company.LegalName, link);
        return await SendAsync(account, company.AccountingFirmId, subject, body, link, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tira o acesso de um cliente a uma empresa; sem mais nenhuma, a conta fica desativada.</summary>
    public async Task<bool> RevokeAsync(Guid actingUserId, Guid companyId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var access = await context.ClientPortalAccess.Include(item => item.Account).ThenInclude(item => item.Companies)
            .SingleOrDefaultAsync(item => item.AccountId == accountId && item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (access is null)
        {
            return false;
        }

        context.ClientPortalAccess.Remove(access);
        if (access.Account.Companies.All(item => item.CompanyId == companyId))
        {
            access.Account.Disable();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<PortalItemView>> ListItemsAsync(Guid actingUserId, Guid companyId, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        return await ItemsOfAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Publica no portal um ficheiro gerado (PDF ou Excel): fica uma fotografia do momento.</summary>
    public async Task<PortalItemView> PublishBytesAsync(
        Guid actingUserId, Guid companyId, PortalCategory category, string title, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var item = new PortalItem(Guid.NewGuid(), companyId, category, title, fileName, contentType, content, content.LongLength, null, actingUserId);
        context.PortalItems.Add(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ItemsOfAsync(companyId, cancellationToken, item.Id).ConfigureAwait(false)).Single();
    }

    /// <summary>Partilha um documento do arquivo digital da empresa com o cliente (o ficheiro lê-se do arquivo).</summary>
    public async Task<PortalItemView> PublishAttachmentAsync(Guid actingUserId, Guid companyId, Guid attachmentId, string? title, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var attachment = await context.DocumentAttachments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == attachmentId && item.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false) ?? throw new KeyNotFoundException("O documento não existe no arquivo da empresa.");
        if (await context.PortalItems.AnyAsync(item => item.AttachmentId == attachmentId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Este documento já está no portal do cliente.");
        }

        var item = new PortalItem(Guid.NewGuid(), companyId, PortalCategory.Documents, string.IsNullOrWhiteSpace(title) ? attachment.FileName : title, attachment.FileName, attachment.ContentType, null, attachment.SizeBytes, attachment.Id, actingUserId);
        context.PortalItems.Add(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ItemsOfAsync(companyId, cancellationToken, item.Id).ConfigureAwait(false)).Single();
    }

    public async Task<bool> DeleteItemAsync(Guid actingUserId, Guid companyId, Guid itemId, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var item = await context.PortalItems.SingleOrDefaultAsync(row => row.Id == itemId && row.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return false;
        }

        context.PortalItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ---- O cliente -------------------------------------------------------------------------------------------

    public async Task<PortalInvitationInfo?> InvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        var account = await FindByTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var names = await CompanyNamesAsync(account.Id, cancellationToken).ConfigureAwait(false);
        return new PortalInvitationInfo(account.Email, account.DisplayName, names.Select(item => item.Name).ToList(), account.PasswordHash is not null);
    }

    /// <summary>
    /// «Esqueci-me da senha»: a uma conta ativa envia um link para escolher outra. Responde sempre da mesma maneira, exista
    /// ou não a conta, e só atende um pedido de 5 em 5 minutos.
    /// </summary>
    public async Task ForgotAsync(string email, string linkBase, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToUpperInvariant();
        var account = await context.ClientPortalAccounts.SingleOrDefaultAsync(item => item.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false);
        if (account is null || account.Status != PortalAccountStatus.Active || account.PasswordHash is null || !account.CanIssueInvite(time.GetUtcNow()))
        {
            return;
        }

        var company = await context.Companies.AsNoTracking()
            .Where(item => item.IsActive && context.ClientPortalAccess.Any(access => access.AccountId == account.Id && access.CompanyId == item.Id))
            .Select(item => new { item.LegalName, item.AccountingFirmId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (company is null)
        {
            return;
        }

        var link = NewInvitation(account, linkBase);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await SendAsync(
                account, company.AccountingFirmId, "Nova senha do portal do cliente",
                $"Olá, {account.DisplayName}.{Environment.NewLine}{Environment.NewLine}Pediu uma nova senha para o portal do cliente. Abra o link e escolha-a (válido por 7 dias):{Environment.NewLine}{link}{Environment.NewLine}{Environment.NewLine}Se não pediu, pode ignorar este email: a sua senha atual continua a valer.",
                link, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// O cliente envia um documento para o gabinete: vai para o arquivo digital da empresa (sem lançamento) e fica registado
    /// em «Enviados por si». Só PDF, imagens e XML, até 15 MB, e no máximo 50 por dia.
    /// </summary>
    public async Task<PortalUploadView?> UploadAsync(Guid accountId, Guid companyId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!await HasAccessAsync(accountId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var name = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || !DocumentAttachment.AllowedContentTypes.ContainsKey(Path.GetExtension(name)))
        {
            throw new ArgumentException("Só se aceitam ficheiros PDF, imagens (PNG, JPG, WEBP, TIFF) e XML.");
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("O ficheiro está vazio.");
        }

        var since = time.GetUtcNow().AddDays(-1);
        if (await context.ClientPortalUploads.CountAsync(item => item.AccountId == accountId && item.UploadedAtUtc > since, cancellationToken).ConfigureAwait(false) >= MaxUploadsPerDay)
        {
            throw new InvalidOperationException($"Atingiu o limite de {MaxUploadsPerDay} documentos por dia: tente amanhã.");
        }

        var sender = await context.ClientPortalAccounts.AsNoTracking().Where(item => item.Id == accountId).Select(item => new { item.DisplayName, item.Email }).SingleAsync(cancellationToken).ConfigureAwait(false);
        var row = await archive.ImportAsync(companyId, name, content, null, $"Enviado pelo cliente {sender.DisplayName} ({sender.Email}) pelo portal.", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Este documento já foi enviado.");
        var upload = new ClientPortalUpload(Guid.NewGuid(), companyId, accountId, row.Id, name, content.LongLength, time.GetUtcNow());
        context.ClientPortalUploads.Add(upload);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PortalUploadView(upload.Id, upload.FileName, upload.SizeBytes, upload.UploadedAtUtc, null);
    }

    /// <summary>Os documentos que a conta enviou para a empresa; <c>null</c> sem acesso.</summary>
    public async Task<IReadOnlyList<PortalUploadView>?> UploadsAsync(Guid accountId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(accountId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await context.ClientPortalUploads.AsNoTracking()
            .Where(item => item.AccountId == accountId && item.CompanyId == companyId)
            .OrderByDescending(item => item.UploadedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(item => new PortalUploadView(item.Id, item.FileName, item.SizeBytes, item.UploadedAtUtc, null)).ToList();
    }

    /// <summary>O que os clientes enviaram para a empresa, com quem enviou, para o gabinete.</summary>
    public async Task<IReadOnlyList<PortalUploadView>> ListUploadsAsync(Guid actingUserId, Guid companyId, CancellationToken cancellationToken = default)
    {
        await RequireMemberAsync(actingUserId, companyId, cancellationToken).ConfigureAwait(false);
        var rows = await context.ClientPortalUploads.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderByDescending(item => item.UploadedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var ids = rows.Select(row => row.AccountId).Distinct().ToList();
        var names = await context.ClientPortalAccounts.AsNoTracking().Where(account => ids.Contains(account.Id)).ToDictionaryAsync(account => account.Id, account => account.DisplayName, cancellationToken).ConfigureAwait(false);
        return rows.Select(item => new PortalUploadView(item.Id, item.FileName, item.SizeBytes, item.UploadedAtUtc, names.GetValueOrDefault(item.AccountId))).ToList();
    }

    /// <summary>Confirma a senha pelo link do convite: a conta fica ativa e o link deixa de valer.</summary>
    public async Task<PortalSession> AcceptAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        var account = await FindByTokenAsync(token, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Este convite expirou ou já foi usado: peça ao gabinete um novo.");
        ValidatePassword(password);
        account.Activate(hasher.HashPassword(account, password));
        account.RecordLogin(time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PortalSession(account.Id, account.DisplayName, account.Email);
    }

    public async Task<PortalLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        const string Wrong = "Email ou senha incorretos.";
        var normalized = (email ?? string.Empty).Trim().ToUpperInvariant();
        var account = await context.ClientPortalAccounts.SingleOrDefaultAsync(item => item.NormalizedEmail == normalized, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (account is null || account.Status != PortalAccountStatus.Active || account.PasswordHash is null)
        {
            return new PortalLoginResult(null, Wrong);
        }

        if (account.IsLocked(now))
        {
            return new PortalLoginResult(null, "Demasiadas tentativas: tente de novo dentro de alguns minutos.");
        }

        var result = hasher.VerifyHashedPassword(account, account.PasswordHash, password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
        {
            account.RecordFailure(now);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new PortalLoginResult(null, Wrong);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.SetPasswordHash(hasher.HashPassword(account, password!));
        }

        account.RecordLogin(now);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PortalLoginResult(new PortalSession(account.Id, account.DisplayName, account.Email), null);
    }

    /// <summary>A conta continua ativa? Verifica-se a cada pedido, para uma conta desativada deixar de ver nada logo.</summary>
    public Task<bool> IsActiveAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        context.ClientPortalAccounts.AsNoTracking().AnyAsync(item => item.Id == accountId && item.Status == PortalAccountStatus.Active, cancellationToken);

    public async Task<PortalSession?> SessionAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await context.ClientPortalAccounts.AsNoTracking()
            .Where(item => item.Id == accountId && item.Status == PortalAccountStatus.Active)
            .Select(item => new PortalSession(item.Id, item.DisplayName, item.Email))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<IReadOnlyList<PortalCompanyView>> CompaniesAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        CompanyNamesAsync(accountId, cancellationToken);

    /// <summary>O que o gabinete publicou para a empresa; <c>null</c> se a conta não tem acesso a ela.</summary>
    public async Task<IReadOnlyList<PortalItemView>?> ItemsAsync(Guid accountId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessAsync(accountId, companyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await ItemsOfAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(string FileName, string ContentType, byte[] Content)?> ContentAsync(Guid accountId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await context.PortalItems.AsNoTracking().SingleOrDefaultAsync(row => row.Id == itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || !await HasAccessAsync(accountId, item.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (item.Content is { } own)
        {
            return (item.FileName, item.ContentType, own);
        }

        var stored = await context.DocumentAttachmentContents.AsNoTracking().SingleOrDefaultAsync(row => row.AttachmentId == item.AttachmentId, cancellationToken).ConfigureAwait(false);
        return stored is null ? null : (item.FileName, item.ContentType, stored.Content);
    }

    // ---- Interno ---------------------------------------------------------------------------------------------

    private async Task RequireMemberAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var member = await context.CompanyUsers.AsNoTracking()
            .AnyAsync(item => item.CompanyId == companyId && item.UserId == userId && item.IsActive && item.Company.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!member)
        {
            throw new KeyNotFoundException("A empresa não existe.");
        }
    }

    private Task<bool> HasAccessAsync(Guid accountId, Guid companyId, CancellationToken cancellationToken) =>
        context.ClientPortalAccess.AsNoTracking().AnyAsync(
            item => item.AccountId == accountId && item.CompanyId == companyId && item.Account.Status == PortalAccountStatus.Active && context.Companies.Any(company => company.Id == item.CompanyId && company.IsActive),
            cancellationToken);

    private async Task<IReadOnlyList<PortalCompanyView>> CompanyNamesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await context.Companies.AsNoTracking()
            .Where(company => company.IsActive && context.ClientPortalAccess.Any(access => access.AccountId == accountId && access.CompanyId == company.Id))
            .OrderBy(company => company.LegalName)
            .Select(company => new { company.Id, company.LegalName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(row => new PortalCompanyView(row.Id, row.LegalName)).ToList();
    }

    private async Task<IReadOnlyList<PortalItemView>> ItemsOfAsync(Guid companyId, CancellationToken cancellationToken, Guid? only = null)
    {
        var rows = await context.PortalItems.AsNoTracking()
            .Where(item => item.CompanyId == companyId && (only == null || item.Id == only))
            .OrderByDescending(item => item.PublishedAtUtc)
            .Select(item => new { item.Id, item.Category, item.Title, item.FileName, item.ContentType, item.SizeBytes, item.PublishedAtUtc, FromArchive = item.AttachmentId != null, item.PublishedBy })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var ids = rows.Where(row => row.PublishedBy != null).Select(row => row.PublishedBy!.Value).Distinct().ToList();
        var names = await context.UserAccounts.AsNoTracking().Where(user => ids.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new PortalItemView(
            row.Id, row.Category.ToString(), row.Title, row.FileName, row.ContentType, row.SizeBytes, row.PublishedAtUtc, row.FromArchive,
            row.PublishedBy is { } by ? names.GetValueOrDefault(by) : null)).ToList();
    }

    private async Task<ClientPortalAccount?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = Hash(token.Trim());
        var account = await context.ClientPortalAccounts.SingleOrDefaultAsync(item => item.InviteTokenHash == hash, cancellationToken).ConfigureAwait(false);
        return account is not null && account.InviteIsValid(time.GetUtcNow()) ? account : null;
    }

    private string NewInvitation(ClientPortalAccount account, string linkBase)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        account.Invite(Hash(token), time.GetUtcNow());
        return $"{linkBase.TrimEnd('/')}/cliente/convite?token={token}";
    }

    private static (string Subject, string Body) InvitationMessage(ClientPortalAccount account, string company, string link) => (
        $"Convite para o portal do cliente — {company}",
        $"Olá, {account.DisplayName}.{Environment.NewLine}{Environment.NewLine}Foi convidado(a) para o portal do cliente de {company}, onde pode consultar os documentos e relatórios que o gabinete lhe disponibiliza.{Environment.NewLine}{Environment.NewLine}Para ativar o acesso, abra o link e escolha uma senha (válido por 7 dias):{Environment.NewLine}{link}{Environment.NewLine}{Environment.NewLine}Se não esperava este email, pode ignorá-lo.");

    private async Task<PortalInviteResult> SendAsync(ClientPortalAccount account, Guid firmId, string subject, string body, string? link, CancellationToken cancellationToken)
    {
        string? error = null;
        var sent = false;
        try
        {
            await mail.SendFromFirmAsync(firmId, [account.Email], subject, body, null, cancellationToken).ConfigureAwait(false);
            sent = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or MailTransportException)
        {
            error = exception.Message;
        }

        return new PortalInviteResult(View(account), sent, sent ? null : link, error);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static PortalUserView View(ClientPortalAccount account) => new(account.Id, account.Email, account.DisplayName, account.Status.ToString(), account.LastLoginAtUtc, account.InviteExpiresAtUtc);

    private static void ValidatePassword(string password)
    {
        if (password is null || password.Length < MinPasswordLength || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ArgumentException($"A senha tem de ter pelo menos {MinPasswordLength} caracteres, com letras e números.");
        }
    }
}
