using AccountingAI.Infrastructure.SqlServer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccountingAI.Infrastructure.SqlServer.Migrations;

[DbContext(typeof(AccountingDbContext))]
partial class AccountingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

        modelBuilder.Entity("AccountingAI.Domain.Organizations.AccountingFirm", entity =>
        {
            entity.Property<Guid>("Id")
                .ValueGeneratedNever()
                .HasColumnType("uniqueidentifier");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("nvarchar(200)");

            entity.Property<byte[]>("RowVersion")
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("rowversion");

            entity.Property<string>("Slug")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("nvarchar(100)");

            entity.HasKey("Id");
            entity.HasIndex("Slug").IsUnique();
            entity.ToTable("AccountingFirms", "organization");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.Company", entity =>
        {
            entity.Property<Guid>("Id")
                .ValueGeneratedNever()
                .HasColumnType("uniqueidentifier");

            entity.Property<Guid>("AccountingFirmId")
                .HasColumnType("uniqueidentifier");

            entity.Property<string>("BaseCurrency")
                .IsRequired()
                .IsFixedLength()
                .HasMaxLength(3)
                .HasColumnType("nchar(3)");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<bool>("IsActive")
                .HasColumnType("bit");

            entity.Property<string>("LegalName")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("nvarchar(200)");

            entity.Property<byte[]>("RowVersion")
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("rowversion");

            entity.Property<string>("TaxRegistrationNumber")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("nvarchar(32)");

            entity.Property<int>("TaxRegion")
                .HasColumnType("int");

            entity.HasKey("Id");
            entity.HasIndex("AccountingFirmId", "TaxRegistrationNumber").IsUnique();
            entity.ToTable("Companies", "organization");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.CompanyUser", entity =>
        {
            entity.Property<Guid>("CompanyId")
                .HasColumnType("uniqueidentifier");

            entity.Property<Guid>("UserId")
                .HasColumnType("uniqueidentifier");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<bool>("IsActive")
                .HasColumnType("bit");

            entity.Property<int>("Role")
                .HasColumnType("int");

            entity.Property<byte[]>("RowVersion")
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("rowversion");

            entity.HasKey("CompanyId", "UserId");
            entity.HasIndex("UserId");
            entity.ToTable("CompanyUsers", "identity");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.UserAccount", entity =>
        {
            entity.Property<Guid>("Id")
                .ValueGeneratedNever()
                .HasColumnType("uniqueidentifier");

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<string>("DisplayName")
                .IsRequired()
                .HasMaxLength(160)
                .HasColumnType("nvarchar(160)");

            entity.Property<string>("Email")
                .IsRequired()
                .HasMaxLength(254)
                .HasColumnType("nvarchar(254)");

            entity.Property<bool>("IsActive")
                .HasColumnType("bit");

            entity.Property<string>("NormalizedEmail")
                .IsRequired()
                .HasMaxLength(254)
                .HasColumnType("nvarchar(254)");

            entity.Property<byte[]>("RowVersion")
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("rowversion");

            entity.HasKey("Id");
            entity.HasIndex("NormalizedEmail").IsUnique();
            entity.ToTable("UserAccounts", "identity");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.UserCompanySettings", entity =>
        {
            entity.Property<Guid>("CompanyId")
                .HasColumnType("uniqueidentifier");

            entity.Property<Guid>("UserId")
                .HasColumnType("uniqueidentifier");

            entity.Property<bool>("CompactMode")
                .HasColumnType("bit");

            entity.Property<string>("Culture")
                .IsRequired()
                .HasMaxLength(16)
                .HasColumnType("nvarchar(16)");

            entity.Property<int?>("DefaultFiscalYear")
                .HasColumnType("int");

            entity.Property<int>("PageSize")
                .HasColumnType("int");

            entity.Property<byte[]>("RowVersion")
                .IsConcurrencyToken()
                .IsRequired()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("rowversion");

            entity.Property<int>("Theme")
                .HasColumnType("int");

            entity.Property<string>("TimeZone")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("nvarchar(64)");

            entity.Property<DateTimeOffset>("UpdatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasKey("CompanyId", "UserId");
            entity.ToTable("UserCompanySettings", "identity");
        });

        modelBuilder.Entity("AccountingAI.Infrastructure.SqlServer.Persistence.FoundationState", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("int");

            SqlServerPropertyBuilderExtensions.UseIdentityColumn(entity.Property<int>("Id"));

            entity.Property<DateTimeOffset>("CreatedAtUtc")
                .ValueGeneratedOnAdd()
                .HasColumnType("datetimeoffset")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property<string>("SchemaVersion")
                .IsRequired()
                .HasMaxLength(32)
                .HasColumnType("nvarchar(32)");

            entity.HasKey("Id");
            entity.ToTable("FoundationState", "infrastructure");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.Company", entity =>
        {
            entity.HasOne("AccountingAI.Domain.Organizations.AccountingFirm", "AccountingFirm")
                .WithMany("Companies")
                .HasForeignKey("AccountingFirmId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.Navigation("AccountingFirm");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.CompanyUser", entity =>
        {
            entity.HasOne("AccountingAI.Domain.Organizations.Company", "Company")
                .WithMany("UserMemberships")
                .HasForeignKey("CompanyId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.HasOne("AccountingAI.Domain.Organizations.UserAccount", "User")
                .WithMany("CompanyMemberships")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.Navigation("Company");
            entity.Navigation("User");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.UserCompanySettings", entity =>
        {
            entity.HasOne("AccountingAI.Domain.Organizations.CompanyUser", "CompanyUser")
                .WithOne("Settings")
                .HasForeignKey("AccountingAI.Domain.Organizations.UserCompanySettings", "CompanyId", "UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            entity.Navigation("CompanyUser");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.AccountingFirm", entity =>
        {
            entity.Navigation("Companies");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.Company", entity =>
        {
            entity.Navigation("UserMemberships");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.CompanyUser", entity =>
        {
            entity.Navigation("Settings");
        });

        modelBuilder.Entity("AccountingAI.Domain.Organizations.UserAccount", entity =>
        {
            entity.Navigation("CompanyMemberships");
        });
    }
}
