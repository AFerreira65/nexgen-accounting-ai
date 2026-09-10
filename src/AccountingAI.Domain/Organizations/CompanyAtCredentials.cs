namespace AccountingAI.Domain.Organizations;

/// <summary>
/// As credenciais da AT de uma empresa (sujeito passivo): o subutilizador e a
/// senha do perfil exigido pela operação e-Fatura. A senha é guardada
/// <em>cifrada</em> — nunca em texto. Uma linha por empresa. Não se usa a senha
/// principal do NIF.
/// </summary>
public sealed class CompanyAtCredentials
{
    public const int SubUserMaxLength = 60;

    private CompanyAtCredentials()
    {
    }

    private CompanyAtCredentials(Guid id, Guid companyId, string subUser, string encryptedPassword)
    {
        Id = id;
        CompanyId = companyId;
        SubUser = subUser;
        EncryptedPassword = encryptedPassword;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>O subutilizador, no formato <c>NIF/n</c> (ex.: <c>500584095/1</c>).</summary>
    public string SubUser { get; private set; } = string.Empty;

    /// <summary>A senha do subutilizador, já cifrada (valor <c>enc:v1:…</c>).</summary>
    public string EncryptedPassword { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = [];

    public static CompanyAtCredentials Create(Guid companyId, string subUser, string encryptedPassword)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A empresa é obrigatória.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);
        return new CompanyAtCredentials(Guid.NewGuid(), companyId, Trim(subUser), encryptedPassword);
    }

    public void Update(string subUser, string encryptedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subUser);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPassword);
        SubUser = Trim(subUser);
        EncryptedPassword = encryptedPassword;
    }

    private static string Trim(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= SubUserMaxLength ? trimmed : trimmed[..SubUserMaxLength];
    }
}
