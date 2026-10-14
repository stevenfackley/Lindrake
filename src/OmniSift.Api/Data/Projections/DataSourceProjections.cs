// ============================================================
// OmniSift.Api — DataSource → DataSourceDto projection
// ============================================================

using System.Linq.Expressions;
using OmniSift.Api.Models;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Data.Projections;

/// <summary>
/// The single <see cref="DataSource"/> → <see cref="DataSourceDto"/> mapping, as an
/// expression EF translates: the chunk count is a <c>COUNT(*)</c> subquery, so no
/// chunk row (content, 384-float embedding) is ever loaded to count it.
/// </summary>
public static class DataSourceProjections
{
    /// <summary>Default page size of the data source list.</summary>
    public const int DefaultTake = 200;

    /// <summary>Largest page the data source list returns.</summary>
    public const int MaxTake = 500;

    public static readonly Expression<Func<DataSource, DataSourceDto>> ToDto = d => new DataSourceDto
    {
        Id = d.Id,
        SourceType = d.SourceType,
        FileName = d.FileName,
        OriginalUrl = d.OriginalUrl,
        Status = d.Status.ToWireString(),
        ErrorMessage = d.ErrorMessage,
        Metadata = d.Metadata,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        ChunkCount = d.Chunks.Count(),
        HasPii = d.HasPii,
        PiiFlags = d.PiiFlags
    };

    /// <summary>
    /// One page of <paramref name="source"/> as DTOs, newest first (ties broken by id
    /// so pages are stable).
    /// </summary>
    public static IQueryable<DataSourceDto> ToDtoPage(this IQueryable<DataSource> source, int skip, int take) =>
        source
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip(skip)
            .Take(take)
            .Select(ToDto);
}
