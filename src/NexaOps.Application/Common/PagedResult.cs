namespace NexaOps.Application.Common;

/// <summary>
/// One page of results plus the metadata a table needs to render pagination.
/// Every list endpoint returns this shape, so the React data grid is written once.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }

    /// <summary>Total matching rows before paging, so the UI can show "1-25 of 431".</summary>
    public int TotalCount { get; }

    /// <summary>One-based page number.</summary>
    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}

/// <summary>Base for list query parameters. Page size is capped to protect the database.</summary>
public abstract class PagedQuery
{
    /// <summary>Hard ceiling on rows per request, enforced regardless of what the client asks for.</summary>
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 25;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Property name to sort by. Validated against an allow-list, never interpolated into SQL.</summary>
    public string? SortBy { get; set; }

    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    public int Skip => (Page - 1) * PageSize;
}

public enum SortDirection
{
    Ascending = 1,
    Descending = 2
}
