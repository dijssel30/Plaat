using System.Collections.Concurrent;

namespace Plaat.Api;

internal sealed class UserStateStore
{
    private readonly ConcurrentDictionary<int, StateContainer> _statesByUserId = new();

    public StateContainer GetOrCreate(int userId)
    {
        return _statesByUserId.GetOrAdd(userId, _ => new StateContainer());
    }
}
