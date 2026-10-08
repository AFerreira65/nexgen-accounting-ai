using System.Net;
using System.Text;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Accounting;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Mail;
using NexGenAccountingAI.Infrastructure.Persistence;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.IntegrationTests;

/// <summary>Email (ADR-0019): contas com e sem OAuth2, envio, receção de documentos e resumo diário.</summary>
[TestClass]
public sealed class EmailTests
{
    private sealed class FakeProtector : IAtSecretProtector
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v2:", StringComparison.Ordinal) == true;

        public string Protect(string plaintext) => "enc:v2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string Unprotect(string? value) => IsProtected(value) ? Encoding.UTF8.GetString(Convert.FromBase64String(value![7..])) : value ?? string.Empty;
    }

    private sealed class FakeSender : IMailSender
    {
        public List<(MailEndpoint Endpoint, OutgoingMail Mail)> Sent { get; } = [];

        public Task SendAsync(MailEndpoint endpoint, OutgoingMail message, CancellationToken cancellationToken = default)
        {
            Sent.Add((endpoint, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReader(params InboundMessage[] messages) : IMailReader
    {
        public Task<int> ReadUnseenAsync(MailEndpoint endpoint, string folder, int maxMessages, Func<InboundMessage, Task<bool>> handler, CancellationToken cancellationToken = default) =>
            ReadAsync(handler);

        public Task TestAsync(MailEndpoint endpoint, string folder, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private async Task<int> ReadAsync(Func<InboundMessage, Task<bool>> handler)
        {
            var handled = 0;
            foreach (var message in messages)
            {
                if (await handler(message).ConfigureAwait(false))
                {
                    handled++;
                }
            }

            return handled;
        }
    }

    private sealed class FakeOAuth : IMailOAuthClient
    {
        public Uri AuthorizationUrl(MailProvider provider, MailOAuthApp app, string redirectUri, string state, string? loginHint) =>
            new($"https://login.example/{provider}?client_id={app.ClientId}&state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri)}");

        public Task<MailOAuthTokens> ExchangeCodeAsync(MailProvider provider, MailOAuthApp app, string redirectUri, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailOAuthTokens("access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1), "ana@empresa.pt"));

        public Task<MailOAuthTokens> RefreshAsync(MailProvider provider, MailOAuthApp app, string refreshToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailOAuthTokens("access-" + refreshToken, refreshToken, DateTimeOffset.UtcNow.AddHours(1), null));
    }

    private static MailAccountInput PasswordAccount(string address, string? password = "senha-de-aplicacao", bool digest = false, bool polling = false) =>
        new("Password", address, "Gabinete", "smtp.exemplo.pt", 587, "StartTls", "imap.exemplo.pt", 993, "Ssl", "INBOX", null, password, digest, polling);

    private static InboundMessage Message(string id, string from, string subject, params (string Name, int Size)[] files) =>
        new(id, from, subject, new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), "Segue em anexo.",
            files.Select(file => new InboundAttachment(file.Name, "application/octet-stream", Enumerable.Repeat((byte)(file.Name.Length + file.Size), file.Size).ToArray())).ToList());

    [TestMethod]
    public async Task TheInboxFilesDocumentsByNifBySenderHistoryAndByHand()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("EmailInbox"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Email", "gabinete-email"));
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            context.Add(new Company(companyA, firmId, "Empresa A, Lda.", "500584095", TaxRegion.Mainland));
            context.Add(new Company(companyB, firmId, "Empresa B, Lda.", "503504564", TaxRegion.Mainland));
            var owner = Guid.NewGuid();
            var viewer = Guid.NewGuid();
            context.Add(new UserAccount(owner, "Dona", "dona@email.invalid"));
            context.Add(new UserAccount(viewer, "Consulta", "consulta@email.invalid"));
            context.Add(new CompanyUser(companyA, owner, CompanyUserRole.Owner));
            context.Add(new CompanyUser(companyB, owner, CompanyUserRole.Owner));
            context.Add(new CompanyUser(companyB, viewer, CompanyUserRole.Viewer));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var protector = new FakeProtector();
            var none = CancellationToken.None;
            var accounts = new MailAccountService(context, protector, new FakeSender(), new FakeReader(), new FakeOAuth());
            var saved = await accounts.SaveAsync(MailAccountPurpose.FirmInbox, firmId, PasswordAccount("faturas@gabinete.pt", polling: true), none).ConfigureAwait(false);
            Assert.IsTrue(saved.HasSecret);
            Assert.IsFalse((await context.MailAccounts.AsNoTracking().SingleAsync().ConfigureAwait(false)).Secret!.Contains("senha-de-aplicacao", StringComparison.Ordinal), "A senha guarda-se cifrada.");

            var reader = new FakeReader(
                Message("<m1@x>", "ana@cliente.pt", "Fatura de setembro", ("fatura-500584095.pdf", 2000)),
                Message("<m2@x>", "rui@outro.pt", "Fatura", ("fatura.pdf", 3000)),
                Message("<m3@x>", "ana@cliente.pt", "Mais uma", ("recibo.pdf", 2500)),
                Message("<m4@x>", "spam@x.pt", "Olá", ("logo.png", 500)),
                Message("<m1@x>", "ana@cliente.pt", "Fatura de setembro", ("fatura-500584095.pdf", 2000)));
            var permissions = new PermissionService(context);
            var inbox = new MailInboxService(context, accounts, reader, new DocumentArchiveService(context), permissions);

            var result = await inbox.PollAsync(firmId, none).ConfigureAwait(false);
            Assert.AreEqual(5, result.Read);
            Assert.AreEqual(3, result.Archived, "m1 e m3 foram para a empresa A; o repetido é lido de novo mas não se guarda outra vez.");
            Assert.AreEqual(1, result.NeedsCompany, "m2 não tem NIF nem remetente conhecido.");
            Assert.AreEqual(1, result.Ignored, "m4 só tem um logótipo.");
            Assert.AreEqual(4, await context.InboundEmails.CountAsync().ConfigureAwait(false), "O email repetido não entra duas vezes.");
            var filed = await context.DocumentAttachments.AsNoTracking().Where(item => item.CompanyId == companyA).Select(item => item.FileName).ToListAsync().ConfigureAwait(false);
            CollectionAssert.AreEquivalent(new[] { "fatura-500584095.pdf", "recibo.pdf" }, filed, "m3 foi para A pelo histórico do remetente.");
            Assert.IsTrue((await context.DocumentAttachments.AsNoTracking().FirstAsync(item => item.CompanyId == companyA).ConfigureAwait(false)).Notes!.StartsWith("Recebido por email de", StringComparison.Ordinal));

            // O que ficou por atribuir vê-se, atribui-se à mão, e só com permissão de juntar documentos.
            var pending = await inbox.ListAsync(owner, null, none).ConfigureAwait(false);
            var waiting = pending.Single();
            Assert.AreEqual("NeedsCompany", waiting.Status);
            Assert.AreEqual("fatura.pdf", waiting.Attachments.Single().FileName);
            Assert.IsNotNull(await inbox.AttachmentAsync(owner, waiting.Attachments.Single().Id, none).ConfigureAwait(false));
            Assert.AreEqual(0, (await inbox.ListAsync(Guid.NewGuid(), null, none).ConfigureAwait(false)).Count, "Quem não é do gabinete não vê nada.");
            await Assert.ThrowsExactlyAsync<OrganizationAccessDeniedException>(() => inbox.AssignAsync(viewer, waiting.Id, companyB, none)).ConfigureAwait(false);
            Assert.IsNull(await inbox.AssignAsync(owner, waiting.Id, Guid.NewGuid(), none).ConfigureAwait(false), "Uma empresa que não é do gabinete.");
            Assert.IsTrue(await inbox.AssignAsync(owner, waiting.Id, companyB, none).ConfigureAwait(false) is true);
            Assert.AreEqual(1, await context.DocumentAttachments.CountAsync(item => item.CompanyId == companyB).ConfigureAwait(false));
            Assert.AreEqual(0, (await inbox.ListAsync(owner, null, none).ConfigureAwait(false)).Count);
            var archivedList = await inbox.ListAsync(owner, "archived", none).ConfigureAwait(false);
            Assert.AreEqual("Empresa B, Lda.", archivedList.Single(item => item.Id == waiting.Id).CompanyName);
            Assert.IsNull(await inbox.AttachmentAsync(owner, waiting.Attachments.Single().Id, none).ConfigureAwait(false), "Depois de arquivado, o conteúdo está no arquivo, não aqui.");
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task AccountsSendWithPasswordsAndWithOAuthAndOnlyForTheUserWhoAsked()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("EmailAccounts"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var userId = Guid.NewGuid();
            var other = Guid.NewGuid();
            context.Add(new UserAccount(userId, "Ana", "ana@email.invalid"));
            context.Add(new UserAccount(other, "Rui", "rui@email.invalid"));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var sender = new FakeSender();
            var protector = new FakeProtector();
            var accounts = new MailAccountService(context, protector, sender, new FakeReader(), new FakeOAuth());
            var oauth = new MailOAuthService(context, protector, new FakeOAuth(), accounts, DataProtectionProvider.Create("email-tests"));
            var none = CancellationToken.None;

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => accounts.SendAsync(userId, ["x@y.pt"], "Assunto", "Texto", null, none)).ConfigureAwait(false);

            // Servidor próprio, com senha de aplicação.
            await accounts.SaveAsync(MailAccountPurpose.UserSending, userId, PasswordAccount("ana@empresa.pt"), none).ConfigureAwait(false);
            await accounts.SendAsync(userId, ["cliente@y.pt"], "Relatório", "Segue.", [new MailAttachment("r.pdf", "application/pdf", [1, 2, 3])], none).ConfigureAwait(false);
            var (endpoint, mail) = sender.Sent.Single();
            Assert.AreEqual("smtp.exemplo.pt", endpoint.Host);
            Assert.AreEqual("senha-de-aplicacao", endpoint.Password, "A senha decifra-se só para enviar.");
            Assert.IsNull(endpoint.AccessToken);
            Assert.AreEqual("ana@empresa.pt", mail.FromAddress);
            Assert.AreEqual(1, mail.Attachments.Count);
            Assert.IsNull((await accounts.GetAsync(MailAccountPurpose.UserSending, userId, none).ConfigureAwait(false)).LastError);

            // Microsoft com OAuth2: sem a aplicação do produtor não há ligação; com ela, o pedido leva um state de uma só pessoa.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                oauth.StartAsync(MailAccountPurpose.UserSending, userId, MailProvider.Microsoft, userId, "https://app/cb", null, none)).ConfigureAwait(false);
            await oauth.SaveAppAsync(MailProvider.Microsoft, "client-1", "segredo-1", "tenant-1", none).ConfigureAwait(false);
            Assert.IsTrue((await accounts.OAuthAvailabilityAsync(none).ConfigureAwait(false)).Microsoft);
            Assert.IsFalse((await accounts.OAuthAvailabilityAsync(none).ConfigureAwait(false)).Google);
            Assert.IsTrue((await oauth.AppsAsync(none).ConfigureAwait(false)).Single(item => item.Provider == "Microsoft").HasSecret);
            await oauth.SaveAppAsync(MailProvider.Microsoft, "client-2", null, null, none).ConfigureAwait(false);
            Assert.AreEqual("client-2", (await context.MailOAuthApps.AsNoTracking().SingleAsync().ConfigureAwait(false)).ClientId);
            Assert.IsTrue((await oauth.AppsAsync(none).ConfigureAwait(false)).Single(item => item.Provider == "Microsoft").Configured, "Um segredo em branco mantém o anterior.");

            var url = await oauth.StartAsync(MailAccountPurpose.UserSending, userId, MailProvider.Microsoft, userId, "https://app/cb", null, none).ConfigureAwait(false);
            var state = Uri.UnescapeDataString(System.Web.HttpUtility.ParseQueryString(url.Query)["state"]!);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => oauth.CompleteAsync(state, "codigo", other, "https://app/cb", none)).ConfigureAwait(false);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => oauth.CompleteAsync("lixo", "codigo", userId, "https://app/cb", none)).ConfigureAwait(false);
            var back = await oauth.CompleteAsync(state, "codigo", userId, "https://app/cb", none).ConfigureAwait(false);
            Assert.AreEqual(MailAccountPurpose.UserSending, back.Purpose);

            var view = await accounts.GetAsync(MailAccountPurpose.UserSending, userId, none).ConfigureAwait(false);
            Assert.AreEqual("Microsoft", view.Provider);
            Assert.AreEqual("ana@empresa.pt", view.Address, "O endereço que o utilizador escreveu mantém-se.");
            Assert.AreEqual("ana@empresa.pt", view.AuthorizedAccount);
            Assert.AreEqual("smtp.office365.com", view.SmtpHost);

            sender.Sent.Clear();
            await accounts.SendAsync(userId, ["cliente@y.pt"], "Relatório", "Segue.", null, none).ConfigureAwait(false);
            Assert.AreEqual("access-refresh-1", sender.Sent.Single().Endpoint.AccessToken, "Com OAuth2 o envio usa um token de acesso renovado, não a senha.");
            Assert.IsNull(sender.Sent.Single().Endpoint.Password);

            // Mudar de tipo obriga a ligar de novo; apagar tira a conta.
            var changed = await accounts.SaveAsync(MailAccountPurpose.UserSending, userId, PasswordAccount("ana@empresa.pt", password: null), none).ConfigureAwait(false);
            Assert.IsFalse(changed.HasSecret);
            Assert.IsTrue(await accounts.DeleteAsync(MailAccountPurpose.UserSending, userId, none).ConfigureAwait(false));
            Assert.IsFalse((await accounts.GetAsync(MailAccountPurpose.UserSending, userId, none).ConfigureAwait(false)).Configured);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task TheDailyDigestListsOverdueObligationsAndOpenTasks()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("EmailDigest"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Resumo", "gabinete-resumo"));
            var company = Guid.NewGuid();
            context.Add(new Company(company, firmId, "Empresa Resumo, Lda.", "500584095", TaxRegion.Mainland));
            var responsible = Guid.NewGuid();
            var bystander = Guid.NewGuid();
            context.Add(new UserAccount(responsible, "Ana Resumo", "ana-resumo@email.invalid"));
            context.Add(new UserAccount(bystander, "Rui Resumo", "rui-resumo@email.invalid"));
            var membership = new CompanyUser(company, responsible, CompanyUserRole.Accountant);
            membership.SetResponsible(true);
            context.Add(membership);
            context.Add(new CompanyUser(company, bystander, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var permissions = new PermissionService(context);
            var calendar = new ObligationCalendarService(context);
            var accounts = new MailAccountService(context, new FakeProtector(), new FakeSender(), new FakeReader(), new FakeOAuth());
            var job = new DailyDigestJob(
                context, accounts, new ObligationConsoleService(context, calendar, permissions),
                new FirmTaskService(context, calendar, TimeProvider.System), new BackgroundJobLog(context));

            var today = new DateOnly(2026, 6, 15);
            var digest = await job.BuildAsync(responsible, today, CancellationToken.None).ConfigureAwait(false);
            Assert.IsNotNull(digest, "O Modelo 22, a IES e os pagamentos por conta aparecem sempre no calendário.");
            StringAssert.Contains(digest.Value.Body, "Bom dia, Ana Resumo.");
            StringAssert.Contains(digest.Value.Body, "Empresa Resumo, Lda.");
            StringAssert.Contains(digest.Value.Body, "A vencer nos próximos 7 dias");
            Assert.IsNull(await job.BuildAsync(bystander, today, CancellationToken.None).ConfigureAwait(false), "Quem não é responsável por nenhuma empresa e não tem tarefas não recebe nada.");
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task TheAssigneeIsEmailedAndTheDashboardCountsOpenAndOverdueTasks()
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>().UseNpgsql(TestDatabase.ConnectionString("EmailTasks"), TestDatabase.Configure).Options;
        await using var context = new AccountingDbContext(options);

        try
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
            var firmId = Guid.NewGuid();
            context.Add(new AccountingFirm(firmId, "Gabinete Tarefas", "gabinete-tarefas-mail"));
            var company = Guid.NewGuid();
            context.Add(new Company(company, firmId, "Empresa Tarefas, Lda.", "500584095", TaxRegion.Mainland));
            var boss = Guid.NewGuid();
            var clerk = Guid.NewGuid();
            context.Add(new UserAccount(boss, "Chefe", "chefe@tarefas.invalid"));
            context.Add(new UserAccount(clerk, "Escriturário", "escriturario@tarefas.invalid"));
            context.Add(new CompanyUser(company, boss, CompanyUserRole.Owner));
            context.Add(new CompanyUser(company, clerk, CompanyUserRole.Accountant));
            await context.SaveChangesAsync().ConfigureAwait(false);

            var sender = new FakeSender();
            var accounts = new MailAccountService(context, new FakeProtector(), sender, new FakeReader(), new FakeOAuth());
            var notifier = new TaskNotifier(context, accounts);
            var calendar = new ObligationCalendarService(context);
            var tasks = new FirmTaskService(context, calendar, TimeProvider.System);
            var none = CancellationToken.None;

            var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
            var created = (await tasks.CreateAsync(boss, company, new FirmTaskInput("Entregar o IVA", "Do mês passado.", yesterday, "high", clerk, null), none).ConfigureAwait(false)).Saved!;
            await tasks.CreateAsync(boss, company, new FirmTaskInput("Arrumar o arquivo", null, null, "low", null, null), none).ConfigureAwait(false);

            Assert.IsFalse(await notifier.NotifyAssignedAsync(boss, created, none).ConfigureAwait(false), "Sem conta de email do chefe, não envia (e não falha).");
            await accounts.SaveAsync(MailAccountPurpose.UserSending, boss, PasswordAccount("chefe@gabinete.pt"), none).ConfigureAwait(false);
            Assert.IsTrue(await notifier.NotifyAssignedAsync(boss, created, none).ConfigureAwait(false));
            var (_, mail) = sender.Sent.Single();
            CollectionAssert.AreEqual(new[] { "escriturario@tarefas.invalid" }, mail.To.ToArray());
            StringAssert.Contains(mail.Subject, "Entregar o IVA");
            StringAssert.Contains(mail.TextBody, "Chefe atribuiu-lhe uma tarefa em Empresa Tarefas, Lda.");
            StringAssert.Contains(mail.TextBody, "Prioridade: alta");
            Assert.AreEqual(clerk, await notifier.AssigneeOfAsync(created.Id, none).ConfigureAwait(false));
            Assert.IsFalse(await notifier.NotifyAssignedAsync(clerk, created, none).ConfigureAwait(false), "Quem se atribui a si próprio não é avisado.");

            // Comentários: qualquer membro lê e escreve; o responsável e quem criou são avisados (menos o autor).
            var comments = new FirmTaskCommentService(context, notifier);
            sender.Sent.Clear();
            var first = await comments.AddAsync(clerk, company, created.Id, "  Já pedi os extratos ao cliente.  ", none).ConfigureAwait(false);
            Assert.AreEqual("Já pedi os extratos ao cliente.", first!.Text);
            Assert.AreEqual(0, sender.Sent.Count, "O escriturário não tem conta de email: não avisa ninguém, nem falha.");
            await accounts.SaveAsync(MailAccountPurpose.UserSending, clerk, PasswordAccount("escriturario@gabinete.pt"), none).ConfigureAwait(false);
            await comments.AddAsync(clerk, company, created.Id, "Falta só a folha de maio.", none).ConfigureAwait(false);
            CollectionAssert.AreEqual(new[] { "chefe@tarefas.invalid" }, sender.Sent.Single().Mail.To.ToArray(), "O chefe criou a tarefa; o escriturário é o autor e o responsável.");
            var listed = await comments.ListAsync(boss, company, created.Id, none).ConfigureAwait(false);
            Assert.AreEqual(2, listed!.Count);
            Assert.IsTrue(listed.All(item => item.AuthorName == "Escriturário" && !item.Mine));
            Assert.IsNull(await comments.ListAsync(Guid.NewGuid(), company, created.Id, none).ConfigureAwait(false), "Quem não é da empresa não vê.");
            Assert.IsNull(await comments.AddAsync(boss, Guid.NewGuid(), created.Id, "x", none).ConfigureAwait(false));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => comments.AddAsync(boss, company, created.Id, "   ", none)).ConfigureAwait(false);

            var dashboard = await new FirmDashboardService(context, calendar, new SuggestedEntryService(context, new JournalPostingService(context))).BuildAsync(boss, DateOnly.FromDateTime(DateTime.Today), none).ConfigureAwait(false);
            var row = dashboard.Companies.Single();
            Assert.AreEqual(2, row.OpenTasks);
            Assert.AreEqual(1, row.OverdueTasks);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    [TestMethod]
    public async Task TheOAuthClientAsksMicrosoftAndGoogleTheRightWay()
    {
        var app = new MailOAuthApp("cliente-1", "segredo-1", "meu-tenant");
        HttpRequestMessage? seen = null;
        string? form = null;
        var idToken = "x." + Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"preferred_username":"ana@tnmc.pt"}""")).TrimEnd('=') + ".y";
        using var http = new HttpClient(new StubHandler(async request =>
        {
            seen = request;
            form = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"access_token":"a-1","refresh_token":"r-1","expires_in":3600,"id_token":"{{idToken}}"}"""),
            };
        }));
        var client = new MailOAuthClient(http);

        var microsoft = client.AuthorizationUrl(MailProvider.Microsoft, app, "https://app/cb", "estado", "ana@tnmc.pt");
        StringAssert.StartsWith(microsoft.ToString(), "https://login.microsoftonline.com/meu-tenant/oauth2/v2.0/authorize?");
        StringAssert.Contains(microsoft.Query, "client_id=cliente-1");
        StringAssert.Contains(microsoft.Query, "SMTP.Send");
        StringAssert.Contains(microsoft.Query, "IMAP.AccessAsUser.All");
        StringAssert.Contains(microsoft.Query, "login_hint=ana%40tnmc.pt");
        var google = client.AuthorizationUrl(MailProvider.Google, app, "https://app/cb", "estado", null);
        StringAssert.StartsWith(google.ToString(), "https://accounts.google.com/o/oauth2/v2/auth?");
        StringAssert.Contains(google.Query, "access_type=offline");
        StringAssert.Contains(google.Query, "mail.google.com");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => client.AuthorizationUrl(MailProvider.Password, app, "https://app/cb", "estado", null));

        var tokens = await client.ExchangeCodeAsync(MailProvider.Microsoft, app, "https://app/cb", "codigo-1").ConfigureAwait(false);
        Assert.AreEqual("https://login.microsoftonline.com/meu-tenant/oauth2/v2.0/token", seen!.RequestUri!.ToString());
        StringAssert.Contains(form!, "grant_type=authorization_code");
        StringAssert.Contains(form!, "code=codigo-1");
        StringAssert.Contains(form!, "client_secret=segredo-1");
        Assert.AreEqual("a-1", tokens.AccessToken);
        Assert.AreEqual("r-1", tokens.RefreshToken);
        Assert.AreEqual("ana@tnmc.pt", tokens.AccountEmail);

        await client.RefreshAsync(MailProvider.Google, app, "r-1").ConfigureAwait(false);
        Assert.AreEqual("https://oauth2.googleapis.com/token", seen.RequestUri.ToString());
        StringAssert.Contains(form!, "grant_type=refresh_token");

        using var refused = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant","error_description":"AADSTS70008: o código expirou.\r\nTrace ID: 123"}"""),
        })));
        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new MailOAuthClient(refused).RefreshAsync(MailProvider.Microsoft, app, "r-1")).ConfigureAwait(false);
        StringAssert.Contains(failure.Message, "o código expirou");
    }
}
