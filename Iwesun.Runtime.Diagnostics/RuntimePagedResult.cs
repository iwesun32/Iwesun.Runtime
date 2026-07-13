namespace Iwesun.Runtime.Diagnostics;

public sealed record RuntimePagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Offset,
    int Limit,
    int Returned,
    bool HasMore)
{
    public static RuntimePagedResult<T> Create(IEnumerable<T> source, int offset = 0, int limit = 100)
    {
        var normalizedOffset = Math.Max(0, offset);
        var normalizedLimit = Math.Clamp(limit, 1, 500);
        var all = source.ToArray();
        var items = all.Skip(normalizedOffset).Take(normalizedLimit).ToArray();
        return new(items, all.Length, normalizedOffset, normalizedLimit, items.Length, normalizedOffset + items.Length < all.Length);
    }
}
