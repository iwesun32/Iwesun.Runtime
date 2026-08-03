namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeStateTransitionEngine
{
    private static readonly RuntimeStateKey[] _anchors =
    [
        RuntimeStateKey.Start,
        RuntimeStateKey.Working,
        RuntimeStateKey.Stop
    ];

    public static bool CanTransition(RuntimeState from, RuntimeState to, RuntimeStateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var fromAnchor = ResolveAnchor(from, catalog);
        var toAnchor = ResolveAnchor(to, catalog);

        if (fromAnchor == toAnchor)
        {
            return true;
        }

        if (fromAnchor is RuntimeStateKey.Start)
        {
            return toAnchor is RuntimeStateKey.Working or RuntimeStateKey.Stop;
        }

        if (fromAnchor is RuntimeStateKey.Working)
        {
            return toAnchor is RuntimeStateKey.Start or RuntimeStateKey.Stop;
        }

        if (fromAnchor is RuntimeStateKey.Stop)
        {
            return toAnchor is RuntimeStateKey.Start;
        }

        return false;
    }

    public static RuntimeStateKey ResolveAnchor(RuntimeState state, RuntimeStateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        foreach (var anchorKey in _anchors)
        {
            var anchorState = catalog.RequireByKey(anchorKey);
            if (state.Is(anchorState))
            {
                return anchorKey;
            }
        }

        return RuntimeStateKey.Start;
    }
}
