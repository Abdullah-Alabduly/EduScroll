using EduScroll.Domain.Entities;
using EduScroll.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RefreshTokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id)
            .ValueGeneratedNever();

        builder.Property(rt => rt.UserId)
            .IsRequired();

        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(rt => rt.CreatedAt)
            .IsRequired();

        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.RevokedAt);

        builder.Property(rt => rt.ReplacedByTokenHash)
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(rt => rt.RevocationReason)
            .HasMaxLength(128);

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion();

        builder.Ignore(rt => rt.IsRevoked);

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_RefreshTokens_TokenHash");

        builder.HasIndex(rt => new { rt.UserId, rt.RevokedAt, rt.ExpiresAt })
            .HasDatabaseName("IX_RefreshTokens_UserId_RevokedAt_ExpiresAt");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
