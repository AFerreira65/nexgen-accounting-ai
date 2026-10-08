using System.Text;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Portal;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>O portal do cliente (ADR-0020): convite por email, senha para ativar, e só o que o gabinete publicou.</summary>
[TestClass]
public sealed class ClientPortalTests
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Protector : IAtSecretProtector
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v2:", StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => "enc:v2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string Unprotect(string? value) => IsProtected(value) ? Encoding.UTF8.GetString(Convert.FromBase64String(value![7..])) : value ?? string.Empty;
    }

    private sealed class Sender : IMailSender
    {
        public List<OutgoingMail> Sent { get; } = [];

        public Task SendAsync(MailEndpoint endpoint, OutgoingMail message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoReader : IMailReader
    {
        public Task<int> ReadUnseenAsync(MailEndpoint endpoint, string folder, int maxMessages, Func<InboundMessage, Task<bool>> handler, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task TestAsync(MailEndpoint endpoint, string folder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOAuth : IMailOAuthClient
    {
        public Uri AuthorizationUrl(MailProvider provider, MailOAuthApp app, string redirectUri, string state, string? loginHint) => new("https://x.invalid");

        public Task<MailOAuthTokens> ExchangeCodeAsync(MailProvider provider, MailOAuthApp app, string redirectUri, string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MailOAuthTokens> RefreshAsync(MailProvider provider, MailOAuthApp app, string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static string TokenIn(string text) => text[(text.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split(['\r', '\n', ' '])[0];

    [TestMethod]
    public async Task AClientIsInvitedByEmailActivatesWithAPasswordAndSeesOnlyWhatWasPublished()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("ClientPortal"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Portal", "gabinete-portal"));
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            context.Add(new Company(companyA, firmId, "Empresa A, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(companyB, firmId, "Empresa B, Lda.", "503504564", TaxRegion.Mainland));
            var staff = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            context.Add(new UserAccount(staff, "Contabilista", "contabilista@portal.invalid"));
            context.Add(new UserAccount(outsider, "Estranho", "estranho@portal.invalid"));
            context.Add(new CompanyUser(companyA, staff, CompanyUserRole.Accountant));
            context.Add(new CompanyUser(companyB, staff, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var clock = new Clock(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
            var sender = new Sender();
            var accounts = new MailAccountService(context, new Protector(), sender, new NoReader(), new NoOAuth());
            var portal = new ClientPortalService(context, accounts, new DocumentArchiveService(context), clock);
            var none = CancellationToken.None;
            const string Base = "https://contas.exemplo.pt";

            // Sem caixa de correio do gabinete, o convite grava-se e devolve o link para enviar à mão.
            var manual = await portal.InviteAsync(staff, companyA, "cliente@empresa-a.pt", "Maria Cliente", Base, none).ConfigureAwait(false);
            Assert.IsFalse(manual.EmailSent);
            StringAssert.Contains(manual.EmailError!, "caixa de correio");
            StringAssert.StartsWith(manual.Link!, $"{Base}/cliente/convite?token=");

            // Com a caixa do gabinete (documentos@…), o convite segue por email.
            await accounts.SaveAsync(MailAccountPurpose.FirmInbox, firmId,
                new MailAccountInput("Password", "documentos@gabinete.pt", "Gabinete", "smtp.office365.com", 587, "StartTls", "outlook.office365.com", 993, "Ssl", "INBOX", null, "senha", false, false), none).ConfigureAwait(false);
            var invite = await portal.ResendAsync(staff, companyA, manual.User.AccountId, Base, none).ConfigureAwait(false);
            Assert.IsTrue(invite.EmailSent);
            Assert.IsNull(invite.Link, "Enviado, o link não volta ao ecrã.");
            var mail = sender.Sent.Single();
            Assert.AreEqual("documentos@gabinete.pt", mail.FromAddress);
            CollectionAssert.AreEqual(new[] { "cliente@empresa-a.pt" }, mail.To.ToArray());
            StringAssert.Contains(mail.TextBody, "Empresa A, Lda.");
            var token = TokenIn(mail.TextBody);
            Assert.AreEqual("Invited", (await portal.ListUsersAsync(staff, companyA, none).ConfigureAwait(false)).Single().Status);

            // O token da primeira tentativa já não vale (o convite novo substituiu-o); o novo mostra para quem é.
            Assert.IsNull(await portal.InvitationAsync(TokenIn(manual.Link!), none).ConfigureAwait(false));
            var info = await portal.InvitationAsync(token, none).ConfigureAwait(false);
            Assert.AreEqual("cliente@empresa-a.pt", info!.Email);
            CollectionAssert.AreEqual(new[] { "Empresa A, Lda." }, info.Companies.ToArray());

            // Antes de confirmar a senha, não entra. Uma senha fraca é recusada; uma boa ativa a conta e o link morre.
            Assert.IsNull((await portal.LoginAsync("cliente@empresa-a.pt", "QualquerCoisa123", none).ConfigureAwait(false)).Session);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => portal.AcceptAsync(token, "curta1", none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => portal.AcceptAsync(token, "soletrassemnumeros", none)).ConfigureAwait(false);
            var session = await portal.AcceptAsync(token, "Senha-do-cliente-2026", none).ConfigureAwait(false);
            Assert.AreEqual("Maria Cliente", session.DisplayName);
            Assert.AreEqual("Active", (await portal.ListUsersAsync(staff, companyA, none).ConfigureAwait(false)).Single().Status);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => portal.AcceptAsync(token, "Senha-do-cliente-2026", none)).ConfigureAwait(false);
            Assert.IsFalse((await context.ClientPortalAccounts.AsNoTracking().SingleAsync().ConfigureAwait(false)).PasswordHash!.Contains("Senha-do-cliente", StringComparison.Ordinal), "A senha guarda-se só com hash.");

            // Entrar: senha errada falha; cinco falhas bloqueiam durante 15 minutos, mesmo com a senha certa.
            Assert.IsNotNull((await portal.LoginAsync("CLIENTE@empresa-a.pt", "Senha-do-cliente-2026", none).ConfigureAwait(false)).Session);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                Assert.IsNull((await portal.LoginAsync("cliente@empresa-a.pt", "errada", none).ConfigureAwait(false)).Session);
            }

            var locked = await portal.LoginAsync("cliente@empresa-a.pt", "Senha-do-cliente-2026", none).ConfigureAwait(false);
            Assert.IsNull(locked.Session);
            StringAssert.Contains(locked.Error!, "Demasiadas tentativas");
            clock.Now = clock.Now.AddMinutes(16);
            Assert.IsNotNull((await portal.LoginAsync("cliente@empresa-a.pt", "Senha-do-cliente-2026", none).ConfigureAwait(false)).Session);

            // O gabinete publica: um relatório (fotografia) e um documento do arquivo. O cliente vê, e só da sua empresa.
            var report = await portal.PublishBytesAsync(staff, companyA, PortalCategory.TrialBalance, "Balancete de setembro", "balancete.pdf", "application/pdf", [1, 2, 3, 4], none).ConfigureAwait(false);
            var archive = new DocumentArchiveService(context);
            var document = await archive.ImportAsync(companyA, "fatura.pdf", Encoding.UTF8.GetBytes("%PDF fatura"), staff, null, none).ConfigureAwait(false);
            var shared = await portal.PublishAttachmentAsync(staff, companyA, document!.Id, "Fatura de setembro", none).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => portal.PublishAttachmentAsync(staff, companyA, document.Id, null, none)).ConfigureAwait(false);
            await portal.PublishBytesAsync(staff, companyB, PortalCategory.Reports, "Relatório da B", "b.pdf", "application/pdf", [9], none).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => portal.PublishBytesAsync(outsider, companyA, PortalCategory.Reports, "x", "x.pdf", "application/pdf", [1], none)).ConfigureAwait(false);

            var items = await portal.ItemsAsync(session.AccountId, companyA, none).ConfigureAwait(false);
            CollectionAssert.AreEquivalent(new[] { "Balancete de setembro", "Fatura de setembro" }, items!.Select(item => item.Title).ToArray());
            Assert.IsTrue(items!.Single(item => item.Title == "Fatura de setembro").FromArchive);
            Assert.AreEqual("Contabilista", items!.First().PublishedByName);
            Assert.IsNull(await portal.ItemsAsync(session.AccountId, companyB, none).ConfigureAwait(false), "Sem acesso à empresa B.");
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, (await portal.ContentAsync(session.AccountId, report.Id, none).ConfigureAwait(false))!.Value.Content);
            Assert.AreEqual("%PDF fatura", Encoding.UTF8.GetString((await portal.ContentAsync(session.AccountId, shared.Id, none).ConfigureAwait(false))!.Value.Content));
            var bItem = (await portal.ListItemsAsync(staff, companyB, none).ConfigureAwait(false)).Single();
            Assert.IsNull(await portal.ContentAsync(session.AccountId, bItem.Id, none).ConfigureAwait(false), "Nem aos ficheiros da empresa B.");
            Assert.AreEqual(1, (await portal.CompaniesAsync(session.AccountId, none).ConfigureAwait(false)).Count, "Só vê a empresa A.");

            // Dar acesso à empresa B a uma conta ativa não gera convite novo; despublicar tira o ficheiro.
            sender.Sent.Clear();
            var grant = await portal.InviteAsync(staff, companyB, "cliente@empresa-a.pt", "", Base, none).ConfigureAwait(false);
            Assert.IsTrue(grant.EmailSent);
            Assert.IsFalse(sender.Sent.Single().TextBody.Contains("token=", StringComparison.Ordinal), "Já tem senha: só ganha o acesso.");
            Assert.AreEqual(1, (await portal.ItemsAsync(session.AccountId, companyB, none).ConfigureAwait(false))!.Count);
            Assert.IsTrue(await portal.DeleteItemAsync(staff, companyA, report.Id, none).ConfigureAwait(false));
            Assert.AreEqual(1, (await portal.ItemsAsync(session.AccountId, companyA, none).ConfigureAwait(false))!.Count);

            // Tirar o acesso a uma empresa; tirando a última, a conta fica desativada e deixa de ver o que for.
            Assert.IsTrue(await portal.RevokeAsync(staff, companyB, session.AccountId, none).ConfigureAwait(false));
            Assert.IsNull(await portal.ItemsAsync(session.AccountId, companyB, none).ConfigureAwait(false));
            Assert.IsTrue(await portal.IsActiveAsync(session.AccountId, none).ConfigureAwait(false));
            Assert.IsTrue(await portal.RevokeAsync(staff, companyA, session.AccountId, none).ConfigureAwait(false));
            Assert.IsFalse(await portal.IsActiveAsync(session.AccountId, none).ConfigureAwait(false));
            Assert.IsNull((await portal.LoginAsync("cliente@empresa-a.pt", "Senha-do-cliente-2026", none).ConfigureAwait(false)).Session);

            // Um convite caduca ao fim de 7 dias.
            var late = await portal.InviteAsync(staff, companyA, "outro@empresa-a.pt", "Outro", Base, none).ConfigureAwait(false);
            var lateToken = TokenIn(sender.Sent.Last().TextBody);
            Assert.IsNotNull(await portal.InvitationAsync(lateToken, none).ConfigureAwait(false));
            clock.Now = clock.Now.AddDays(8);
            Assert.IsNull(await portal.InvitationAsync(lateToken, none).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => portal.AcceptAsync(lateToken, "Senha-do-cliente-2026", none)).ConfigureAwait(false);
            Assert.IsTrue(late.EmailSent);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task AClientSendsDocumentsAndCanAskForANewPassword()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("ClientPortalUploads"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Envios", "gabinete-envios"));
            var company = Guid.NewGuid();
            var other = Guid.NewGuid();
            context.Add(new Company(company, firmId, "Empresa Envios, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(other, firmId, "Outra Empresa, Lda.", "503504564", TaxRegion.Mainland));
            var staff = Guid.NewGuid();
            context.Add(new UserAccount(staff, "Contabilista", "contabilista@envios.invalid"));
            context.Add(new CompanyUser(company, staff, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var clock = new Clock(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
            var sender = new Sender();
            var accounts = new MailAccountService(context, new Protector(), sender, new NoReader(), new NoOAuth());
            await accounts.SaveAsync(MailAccountPurpose.FirmInbox, firmId,
                new MailAccountInput("Password", "documentos@gabinete.pt", "Gabinete", "smtp.office365.com", 587, "StartTls", "outlook.office365.com", 993, "Ssl", "INBOX", null, "senha", false, false), CancellationToken.None).ConfigureAwait(false);
            var portal = new ClientPortalService(context, accounts, new DocumentArchiveService(context), clock);
            var none = CancellationToken.None;
            const string Base = "https://contas.exemplo.pt";

            await portal.InviteAsync(staff, company, "cliente@envios.pt", "Rita Cliente", Base, none).ConfigureAwait(false);
            var session = await portal.AcceptAsync(TokenIn(sender.Sent.Single().TextBody), "Senha-do-cliente-2026", none).ConfigureAwait(false);

            // Enviar um documento: vai para o arquivo da empresa, fica registado, e o mesmo ficheiro não entra duas vezes.
            var sent = await portal.UploadAsync(session.AccountId, company, "C:\\Users\\rita\\fatura-outubro.pdf", Encoding.UTF8.GetBytes("%PDF outubro"), none).ConfigureAwait(false);
            Assert.AreEqual("fatura-outubro.pdf", sent!.FileName, "Só o nome do ficheiro, sem o caminho do cliente.");
            var filed = await context.DocumentAttachments.AsNoTracking().SingleAsync().ConfigureAwait(false);
            Assert.AreEqual(company, filed.CompanyId);
            StringAssert.Contains(filed.Notes!, "Rita Cliente");
            Assert.IsNull(filed.UploadedBy);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => portal.UploadAsync(session.AccountId, company, "outra.pdf", Encoding.UTF8.GetBytes("%PDF outubro"), none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => portal.UploadAsync(session.AccountId, company, "programa.exe", [1, 2], none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => portal.UploadAsync(session.AccountId, company, "vazio.pdf", [], none)).ConfigureAwait(false);
            Assert.IsNull(await portal.UploadAsync(session.AccountId, other, "x.pdf", [1], none).ConfigureAwait(false), "Sem acesso à outra empresa.");

            Assert.AreEqual(1, (await portal.UploadsAsync(session.AccountId, company, none).ConfigureAwait(false))!.Count);
            Assert.IsNull(await portal.UploadsAsync(session.AccountId, other, none).ConfigureAwait(false));
            var forStaff = await portal.ListUploadsAsync(staff, company, none).ConfigureAwait(false);
            Assert.AreEqual("Rita Cliente", forStaff.Single().SenderName);
            Assert.IsNull((await portal.UploadsAsync(session.AccountId, company, none).ConfigureAwait(false))!.Single().SenderName, "O cliente não vê o nome de ninguém.");

            // O limite diário.
            for (var index = 0; index < ClientPortalService.MaxUploadsPerDay - 1; index++)
            {
                await portal.UploadAsync(session.AccountId, company, $"doc-{index}.pdf", Encoding.UTF8.GetBytes($"%PDF {index}"), none).ConfigureAwait(false);
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => portal.UploadAsync(session.AccountId, company, "mais.pdf", Encoding.UTF8.GetBytes("%PDF mais"), none)).ConfigureAwait(false);
            clock.Now = clock.Now.AddDays(1).AddMinutes(1);
            Assert.IsNotNull(await portal.UploadAsync(session.AccountId, company, "mais.pdf", Encoding.UTF8.GetBytes("%PDF mais"), none).ConfigureAwait(false));

            // Esqueci-me da senha: um link novo, só a contas ativas, de 5 em 5 minutos, e a resposta nunca revela nada.
            sender.Sent.Clear();
            await portal.ForgotAsync("naoexiste@x.pt", Base, none).ConfigureAwait(false);
            Assert.AreEqual(0, sender.Sent.Count);
            await portal.ForgotAsync("CLIENTE@envios.pt", Base, none).ConfigureAwait(false);
            var mail = sender.Sent.Single();
            StringAssert.Contains(mail.Subject, "Nova senha");
            var resetToken = TokenIn(mail.TextBody);
            Assert.IsTrue((await portal.InvitationAsync(resetToken, none).ConfigureAwait(false))!.IsReset);
            await portal.ForgotAsync("cliente@envios.pt", Base, none).ConfigureAwait(false);
            Assert.AreEqual(1, sender.Sent.Count, "Dois pedidos seguidos geram um só email.");

            // A senha antiga continua a valer até se escolher a nova; depois só a nova.
            Assert.IsNotNull((await portal.LoginAsync("cliente@envios.pt", "Senha-do-cliente-2026", none).ConfigureAwait(false)).Session);
            await portal.AcceptAsync(resetToken, "Outra-senha-2027", none).ConfigureAwait(false);
            Assert.IsNull((await portal.LoginAsync("cliente@envios.pt", "Senha-do-cliente-2026", none).ConfigureAwait(false)).Session);
            Assert.IsNotNull((await portal.LoginAsync("cliente@envios.pt", "Outra-senha-2027", none).ConfigureAwait(false)).Session);
            clock.Now = clock.Now.AddMinutes(6);
            await portal.ForgotAsync("cliente@envios.pt", Base, none).ConfigureAwait(false);
            Assert.AreEqual(2, sender.Sent.Count);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }
}
