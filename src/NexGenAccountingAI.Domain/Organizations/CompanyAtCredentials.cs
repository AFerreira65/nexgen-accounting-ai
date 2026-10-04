namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// As credenciais e as definições da AT de uma empresa (sujeito passivo): o
/// subutilizador e a senha do perfil exigido pela operação e-Fatura, a
/// periodicidade da declaração periódica de IVA, e o ambiente da AT
/// (testes/produção) em que as chamadas dessa empresa devem correr. A senha é
/// guardada <em>cifrada</em> — nunca em texto. Uma linha por empresa. Não se
/// usa a senha principal do NIF.
/// </summary>
public sealed class CompanyAtCredentials
{
    public const int SubUserMaxLength = 60;

    private CompanyAtCredentials()
    {
    }

    private CompanyAtCredentials(
        Guid id, Guid companyId, string subUser, string encryptedPassword,
        VatFilingPeriodicity periodicity, AtEnvironment environment)
    {
        Id = id;
        CompanyId = companyId;
        SubUser = subUser;
        EncryptedPassword = encryptedPassword;
        Periodicity = periodicity;
        Environment = environment;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>O subutilizador, no formato <c>NIF/n</c> (ex.: <c>500584095/1</c>).</summary>
    public string SubUser { get; private set; } = string.Empty;

    /// <summary>A senha do subutilizador, já cifrada (valor <c>enc:v1:…</c>).</summary>
    public string EncryptedPassword { get; private set; } = string.Empty;

    /// <summary>A periodicidade da declaração periódica de IVA desta empresa.</summary>
    public VatFilingPeriodicity Periodicity { get; private set; } = VatFilingPeriodicity.Mensal;

    /// <summary>O ambiente da AT (testes/produção) em que as chamadas desta empresa devem correr.</summary>
    public AtEnvironment Environment { get; private set; } = AtEnvironment.Testes;

    public uint RowVersion { get; private set; }

    public static CompanyAtCredentials Create(
        Guid companyId, string subUser, string encryptedPassword,
        VatFilingPeriodicity periodicity, AtEnvironment environment)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);
        return new CompanyAtCredentials(Guid.NewGuid(), companyId, Trim(subUser), encryptedPassword, periodicity, environment);
    }

    public void Update(
        string subUser, string encryptedPassword, VatFilingPeriodicity periodicity, AtEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);
        SubUser = Trim(subUser);
        EncryptedPassword = encryptedPassword;
        Periodicity = periodicity;
        Environment = environment;
    }

    private static string Trim(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= SubUserMaxLength ? trimmed : trimmed[..SubUserMaxLength];
    }
}
