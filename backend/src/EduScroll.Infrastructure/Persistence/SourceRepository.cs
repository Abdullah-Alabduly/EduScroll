using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class SourceRepository(ApplicationDbContext dbContext) : ISourceRepository
{
    public async Task AddAsync(Source source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        await dbContext.Sources.AddAsync(source, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Source?> GetByIdForOwnerAsync(
        Guid sourceId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.Sources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Id == sourceId && s.OwnerUserId == ownerUserId,
                cancellationToken);
    }

    public async Task<(IReadOnlyList<SourceListItem> Items, int TotalCount)> GetPagedForOwnerAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        int previewMaxLength,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Sources
            .AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId);

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return ([], 0);
        }

        // Fetch slightly more raw characters than previewMaxLength so whitespace normalization
        // still has enough text without pulling the full ExtractedText column across the wire.
        var rawPreviewChars = Math.Clamp(previewMaxLength + 120, 80, 1200);
        var skip = (page - 1) * pageSize;

        var items = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenByDescending(s => s.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(s => new SourceListItem(
                s.Id,
                s.Title,
                s.SourceType,
                s.OriginalUrl,
                s.OriginalFileName,
                s.ContentLength,
                s.ExtractedText.Length > rawPreviewChars
                    ? s.ExtractedText.Substring(0, rawPreviewChars)
                    : s.ExtractedText,
                s.Status,
                s.CreatedAtUtc,
                s.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> DeleteForOwnerAsync(
        Guid sourceId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var deletedRows = await dbContext.Sources
            .Where(s => s.Id == sourceId && s.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(cancellationToken);

        return deletedRows > 0;
    }
}
