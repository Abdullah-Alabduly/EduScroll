using EduScroll.Application.Authentication.Ports;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class RefreshTokenStore(ApplicationDbContext dbContext) : IRefreshTokenStore
{
    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        await dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<bool> TryRotateAsync(
        RefreshToken currentToken,
        RefreshToken replacementToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentToken);
        ArgumentNullException.ThrowIfNull(replacementToken);

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            if (dbContext.Database.IsRelational())
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await PersistRotationAsync(currentToken, replacementToken, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    return false;
                }
            }

            try
            {
                await PersistRotationAsync(currentToken, replacementToken, cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                return false;
            }
        });
    }

    public async Task<bool> TryRevokeAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        try
        {
            if (dbContext.Entry(token).State == EntityState.Detached)
            {
                dbContext.RefreshTokens.Attach(token);
                dbContext.Entry(token).State = EntityState.Modified;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task PersistRotationAsync(
        RefreshToken currentToken,
        RefreshToken replacementToken,
        CancellationToken cancellationToken)
    {
        if (dbContext.Entry(currentToken).State == EntityState.Detached)
        {
            dbContext.RefreshTokens.Attach(currentToken);
            dbContext.Entry(currentToken).State = EntityState.Modified;
        }

        if (dbContext.Entry(replacementToken).State == EntityState.Detached)
        {
            await dbContext.RefreshTokens.AddAsync(replacementToken, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
