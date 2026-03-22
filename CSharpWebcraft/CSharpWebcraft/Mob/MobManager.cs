using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

public class MobManager
{
    private readonly List<MobBase> _mobs = new();
    private readonly List<CubeSlime> _slimes = new(); // Separate list for flocking
    private readonly WorldManager _world;

    private float _spawnTimer;
    private const float SpawnInterval = 3f;
    private const int MaxMobs = 30;
    private const float SpawnMinDist = 20f;
    private const float SpawnMaxDist = 50f;

    public int MobCount => _mobs.Count;
    public IReadOnlyList<MobBase> Mobs => _mobs;

    public MobManager(WorldManager world)
    {
        _world = world;
    }

    public void Update(float dt, Vector3 playerPos)
    {
        // Spawn timer
        _spawnTimer -= dt;
        if (_spawnTimer <= 0 && _mobs.Count < MaxMobs)
        {
            TrySpawnMob(playerPos);
            _spawnTimer = SpawnInterval;
        }

        // Update flocking for slimes
        FlockingSystem.Update(_slimes);

        // Update all mobs
        for (int i = _mobs.Count - 1; i >= 0; i--)
        {
            _mobs[i].Update(dt, _world, playerPos);

            if (_mobs[i].MarkedForRemoval)
            {
                if (_mobs[i] is CubeSlime slime)
                    _slimes.Remove(slime);
                _mobs.RemoveAt(i);
            }
        }
    }

    private void TrySpawnMob(Vector3 playerPos)
    {
        // Pick random position around player
        float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
        float dist = SpawnMinDist + Random.Shared.NextSingle() * (SpawnMaxDist - SpawnMinDist);

        float spawnX = playerPos.X + MathF.Cos(angle) * dist;
        float spawnZ = playerPos.Z + MathF.Sin(angle) * dist;

        // Find ground level
        int ix = (int)MathF.Floor(spawnX);
        int iz = (int)MathF.Floor(spawnZ);
        int groundY = _world.GetColumnHeight(ix, iz);

        // Don't spawn on water or below water level
        byte surfaceBlock = _world.GetBlockAt(ix, groundY, iz);
        if (surfaceBlock == 9 || surfaceBlock == 0 || groundY < GameConfig.WATER_LEVEL)
            return;

        // Check 2 blocks above are air
        if (_world.GetBlockAt(ix, groundY + 1, iz) != 0 || _world.GetBlockAt(ix, groundY + 2, iz) != 0)
            return;

        Vector3 spawnPos = new(spawnX, groundY + 1.5f, spawnZ);

        // Decide mob type (70% slime, 30% spider)
        if (Random.Shared.NextSingle() < 0.7f)
        {
            // Spawn slime group (1-3)
            int count = 1 + Random.Shared.Next(3);
            for (int i = 0; i < count && _mobs.Count < MaxMobs; i++)
            {
                float offsetX = (Random.Shared.NextSingle() - 0.5f) * 3f;
                float offsetZ = (Random.Shared.NextSingle() - 0.5f) * 3f;
                float size = 0.5f + Random.Shared.NextSingle() * 1f; // 0.5 to 1.5

                var slime = new CubeSlime(spawnPos + new Vector3(offsetX, 0, offsetZ), size);
                _mobs.Add(slime);
                _slimes.Add(slime);
            }
        }
        else
        {
            var spider = new SpiderMob(spawnPos);
            _mobs.Add(spider);
        }
    }

    /// <summary>
    /// Check if player attack hits any mob. Returns the hit mob or null.
    /// </summary>
    public MobBase? TryHitMob(Vector3 origin, Vector3 direction, float range)
    {
        float bestDist = range;
        MobBase? hitMob = null;

        foreach (var mob in _mobs)
        {
            if (!mob.IsAlive) continue;

            // Simple sphere-ray intersection
            Vector3 toMob = mob.Position - origin;
            float proj = Vector3.Dot(toMob, direction);
            if (proj < 0 || proj > range) continue;

            Vector3 closest = origin + direction * proj;
            float distSq = (closest - mob.Position).LengthSquared;
            float hitRadius = MathF.Max(mob.BodyWidth, mob.BodyHeight) * 0.6f;

            if (distSq < hitRadius * hitRadius && proj < bestDist)
            {
                bestDist = proj;
                hitMob = mob;
            }
        }

        return hitMob;
    }

    /// <summary>
    /// Check if any mob is close enough to attack the player.
    /// Returns total damage dealt this frame.
    /// </summary>
    public int CheckMobAttacks(Vector3 playerPos)
    {
        int totalDamage = 0;
        foreach (var mob in _mobs)
        {
            if (mob.State != MobState.Attack) continue;

            float distSq = (mob.Position - playerPos).LengthSquared;
            if (distSq < mob.AttackRange * mob.AttackRange)
            {
                totalDamage += mob.AttackDamage;
                // Reset attack state so it doesn't deal damage every frame
                mob.TransitionTo(MobState.Chase);
            }
        }
        return totalDamage;
    }
}
