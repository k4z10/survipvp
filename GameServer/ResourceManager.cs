using GameShared;

namespace GameServer;

public class ResourceManager
{
    private readonly Dictionary<int, ResourceState> _resources = new();
    private int _nextId = 0;

    public ResourceManager()
    {
        GenerateResources();
    }

    private void GenerateResources()
    {
        var rand = new Random();
        for (int i = 0; i < 1000; i++)
        {
            float x = rand.Next(0, 500);
            float y = rand.Next(0, 500);

            int[] distribution = [0, 0, 0, 0, 0, 0, 0, 1, 1, 2];

            var res = new ResourceState
            {
                Id = _nextId++,
                Type = (ResourceType)distribution[rand.Next(distribution.Length)],
                X = x,
                Y = y,
                IsActive = true
            };
            _resources[res.Id] = res;
        }
    }

    public List<ResourceState> GetAllResources()
    {
        return _resources.Values.Where(r => r.IsActive).ToList();
    }

    public bool TryGather(int resourceId, float playerX, float playerY, out ResourceType type)
    {
        type = ResourceType.Tree;
        if (!_resources.TryGetValue(resourceId, out var res)) return false;
        if (!res.IsActive) return false;

        float dx = playerX - res.X;
        float dy = playerY - res.Y;
        if (dx*dx + dy*dy > 2.0f * 2.0f) return false; // Too far

        res.IsActive = false;
        _resources[resourceId] = res;
        type = res.Type;
        return true;
    }
}
