using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

public enum MobState
{
    Idle,
    Wander,
    Chase,
    Attack,
    Hurt,
    Death
}

public abstract class MobBase
{
    // Position & movement
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;         // Horizontal rotation in radians
    public float BodyWidth;   // Collision radius
    public float BodyHeight;  // Collision height

    // State machine
    public MobState State { get; protected set; } = MobState.Idle;
    protected float StateTimer;

    // Stats
    public int Health;
    public int MaxHealth;
    public float Speed;
    public int AttackDamage;
    public float AttackRange;
    public float DetectRange;
    public float DespawnRange = 80f;

    // Physics
    public bool IsOnGround;
    public bool IsAlive => Health > 0;
    public bool MarkedForRemoval;

    // Audio
    public float IdleSoundTimer;
    public string? IdleSoundName;

    // Hurt flash
    public float HurtTimer;
    private const float HurtDuration = 0.3f;
    private const float HurtKnockback = 0.15f;

    // Death
    protected float DeathTimer;
    private const float DeathDuration = 0.8f;

    // Wander
    protected float WanderAngle;
    protected float WanderTimer;

    // Rendering
    public abstract MobMeshData BuildMesh(float skyMultiplier, WorldManager world);

    protected MobBase(Vector3 position, int health, float speed)
    {
        Position = position;
        Health = health;
        MaxHealth = health;
        Speed = speed;
    }

    public virtual void Update(float dt, WorldManager world, Vector3 playerPos)
    {
        if (MarkedForRemoval) return;

        HurtTimer = MathF.Max(0, HurtTimer - dt);

        switch (State)
        {
            case MobState.Idle:
                UpdateIdle(dt, world, playerPos);
                break;
            case MobState.Wander:
                UpdateWander(dt, world, playerPos);
                break;
            case MobState.Chase:
                UpdateChase(dt, world, playerPos);
                break;
            case MobState.Attack:
                UpdateAttack(dt, world, playerPos);
                break;
            case MobState.Hurt:
                UpdateHurt(dt, world, playerPos);
                break;
            case MobState.Death:
                UpdateDeath(dt);
                break;
        }

        // Apply physics
        ApplyGravity(dt);
        ApplyMovement(dt, world);

        // Despawn check
        float distSq = (Position - playerPos).LengthSquared;
        if (distSq > DespawnRange * DespawnRange)
            MarkedForRemoval = true;
    }

    protected virtual void UpdateIdle(float dt, WorldManager world, Vector3 playerPos)
    {
        StateTimer -= dt;
        if (StateTimer <= 0)
        {
            // Randomly decide to wander
            WanderAngle = Yaw + (Random.Shared.NextSingle() - 0.5f) * MathF.PI;
            WanderTimer = 2f + Random.Shared.NextSingle() * 3f;
            TransitionTo(MobState.Wander);
        }

        CheckForPlayer(playerPos);
    }

    protected virtual void UpdateWander(float dt, WorldManager world, Vector3 playerPos)
    {
        WanderTimer -= dt;

        // Turn toward wander direction
        Yaw = LerpAngle(Yaw, WanderAngle, dt * 3f);

        // Move forward
        float moveX = MathF.Cos(Yaw) * Speed * 0.4f;
        float moveZ = MathF.Sin(Yaw) * Speed * 0.4f;
        Velocity.X = moveX;
        Velocity.Z = moveZ;

        if (WanderTimer <= 0)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 1f + Random.Shared.NextSingle() * 2f;
            TransitionTo(MobState.Idle);
        }

        CheckForPlayer(playerPos);
    }

    protected virtual void UpdateChase(float dt, WorldManager world, Vector3 playerPos)
    {
        float dx = playerPos.X - Position.X;
        float dz = playerPos.Z - Position.Z;
        float distSq = dx * dx + dz * dz;
        float targetAngle = MathF.Atan2(dz, dx);

        Yaw = LerpAngle(Yaw, targetAngle, dt * 5f);

        // Move toward player
        Velocity.X = MathF.Cos(Yaw) * Speed;
        Velocity.Z = MathF.Sin(Yaw) * Speed;

        // Jump if blocked
        if (IsOnGround && IsBlockedAhead(world))
            Velocity.Y = GameConfig.JUMP_FORCE * 0.7f;

        // Attack if close enough
        if (distSq < AttackRange * AttackRange)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 0.5f;
            TransitionTo(MobState.Attack);
        }

        // Lose interest if too far
        if (distSq > DetectRange * DetectRange * 1.5f)
        {
            Velocity.X = 0;
            Velocity.Z = 0;
            StateTimer = 1f;
            TransitionTo(MobState.Idle);
        }
    }

    protected virtual void UpdateAttack(float dt, WorldManager world, Vector3 playerPos)
    {
        StateTimer -= dt;
        if (StateTimer <= 0)
        {
            // Return to chase
            TransitionTo(MobState.Chase);
        }
    }

    protected virtual void UpdateHurt(float dt, WorldManager world, Vector3 playerPos)
    {
        StateTimer -= dt;
        if (StateTimer <= 0)
        {
            if (Health <= 0)
            {
                DeathTimer = DeathDuration;
                TransitionTo(MobState.Death);
            }
            else
            {
                TransitionTo(MobState.Chase);
            }
        }
    }

    private void UpdateDeath(float dt)
    {
        DeathTimer -= dt;
        Velocity.X = 0;
        Velocity.Z = 0;
        if (DeathTimer <= 0)
            MarkedForRemoval = true;
    }

    public virtual void TakeDamage(int damage, Vector3 fromDirection)
    {
        if (!IsAlive || State == MobState.Death) return;

        Health -= damage;
        HurtTimer = HurtDuration;

        // Knockback
        Vector3 kb = Vector3.Normalize(new Vector3(fromDirection.X, 0, fromDirection.Z));
        Velocity.X = -kb.X * HurtKnockback;
        Velocity.Z = -kb.Z * HurtKnockback;
        Velocity.Y = 0.15f;

        StateTimer = HurtDuration;
        TransitionTo(MobState.Hurt);
    }

    internal void TransitionTo(MobState newState)
    {
        State = newState;
    }

    protected virtual void CheckForPlayer(Vector3 playerPos)
    {
        float dx = playerPos.X - Position.X;
        float dz = playerPos.Z - Position.Z;
        if (dx * dx + dz * dz < DetectRange * DetectRange)
            TransitionTo(MobState.Chase);
    }

    protected virtual void ApplyGravity(float dt)
    {
        if (!IsOnGround)
            Velocity.Y -= GameConfig.GRAVITY;
        Velocity.Y = MathF.Max(Velocity.Y, -GameConfig.GRAVITY * 20);
    }

    protected virtual void ApplyMovement(float dt, WorldManager world)
    {
        // Horizontal movement with collision
        float newX = Position.X + Velocity.X;
        float newZ = Position.Z + Velocity.Z;

        if (!CheckMobCollision(world, newX, Position.Y, Position.Z))
            Position.X = newX;
        else
            Velocity.X = 0;

        if (!CheckMobCollision(world, Position.X, Position.Y, newZ))
            Position.Z = newZ;
        else
            Velocity.Z = 0;

        // Vertical movement
        float newY = Position.Y + Velocity.Y;
        float feetY = newY - BodyHeight * 0.5f;

        if (Velocity.Y <= 0 && CheckMobCollision(world, Position.X, feetY, Position.Z))
        {
            // Land on ground
            Position.Y = MathF.Floor(feetY) + 1f + BodyHeight * 0.5f;
            Velocity.Y = 0;
            IsOnGround = true;
        }
        else if (Velocity.Y > 0 && CheckMobCollision(world, Position.X, newY + BodyHeight * 0.5f, Position.Z))
        {
            Velocity.Y = 0;
            IsOnGround = false;
        }
        else
        {
            Position.Y = newY;
            IsOnGround = false;
        }

        // Clamp to world bounds
        Position.Y = MathF.Max(Position.Y, BodyHeight * 0.5f + 1f);
    }

    protected bool CheckMobCollision(WorldManager world, float x, float y, float z)
    {
        float halfW = BodyWidth * 0.5f;
        float halfH = BodyHeight * 0.5f;

        // Check corners + center at feet and head
        float[] xOffsets = { -halfW, halfW, 0 };
        float[] zOffsets = { -halfW, halfW, 0 };
        float[] yOffsets = { -halfH, halfH, 0 };

        foreach (float dy in yOffsets)
        foreach (float dx in xOffsets)
        foreach (float dz in zOffsets)
        {
            int wx = (int)MathF.Floor(x + dx);
            int wy = (int)MathF.Floor(y + dy);
            int wz = (int)MathF.Floor(z + dz);
            byte block = world.GetBlockAt(wx, wy, wz);
            if (!BlockRegistry.IsPassable(block))
                return true;
        }
        return false;
    }

    protected bool IsBlockedAhead(WorldManager world)
    {
        float checkX = Position.X + MathF.Cos(Yaw) * (BodyWidth * 0.5f + 0.3f);
        float checkZ = Position.Z + MathF.Sin(Yaw) * (BodyWidth * 0.5f + 0.3f);
        int wx = (int)MathF.Floor(checkX);
        int wy = (int)MathF.Floor(Position.Y - BodyHeight * 0.5f);
        int wz = (int)MathF.Floor(checkZ);
        byte block = world.GetBlockAt(wx, wy, wz);
        return !BlockRegistry.IsPassable(block);
    }

    protected static float LerpAngle(float from, float to, float t)
    {
        float diff = to - from;
        while (diff > MathF.PI) diff -= MathF.PI * 2;
        while (diff < -MathF.PI) diff += MathF.PI * 2;
        return from + diff * MathF.Min(t, 1f);
    }

    /// <summary>
    /// Get sky and block brightness at mob position for lighting.
    /// </summary>
    protected (float skyBri, float blockBri) GetLighting(WorldManager world)
    {
        int wx = (int)MathF.Floor(Position.X);
        int wy = (int)MathF.Floor(Position.Y);
        int wz = (int)MathF.Floor(Position.Z);

        int skyLight = world.GetSkyLightAt(wx, wy, wz);
        int blockLight = world.GetBlockLightAt(wx, wy, wz);

        float skyBri = skyLight / (float)GameConfig.MAX_LIGHT_LEVEL;
        float blockBri = blockLight / (float)GameConfig.MAX_LIGHT_LEVEL;

        // Apply gamma
        skyBri = MathF.Pow(skyBri, 1f / GameConfig.LIGHT_GAMMA);
        blockBri = MathF.Pow(blockBri, 1f / GameConfig.LIGHT_GAMMA);

        return (skyBri, blockBri);
    }
}

/// <summary>
/// Raw mesh data for a mob, ready to upload to GPU.
/// Uses the same vertex format as ChunkMesh (13 floats per vertex).
/// </summary>
public struct MobMeshData
{
    public float[] Vertices;
    public int VertexCount;

    public MobMeshData(float[] vertices, int vertexCount)
    {
        Vertices = vertices;
        VertexCount = vertexCount;
    }
}
