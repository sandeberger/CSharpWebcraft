using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

public class SpiderMob : MobBase
{
    // --- Body geometry (matching JS bodySize=1.5) ---
    private const float BodySize = 1.5f;
    private const float BodyHW = BodySize * 0.5f;  // Forward half-length (X)
    private const float BodyHH = BodySize * 0.3f;  // Vertical half-height (Y)
    private const float BodyHL = BodySize * 0.3f;   // Sideways half-width (Z)
    private const float BodyYOffset = BodySize * 0.8f; // Height above ground

    // --- Leg constants (matching JS) ---
    private const int LegCount = 6;
    private const float MaxLegLength = 3.0f;
    private const float UpperLegLength = MaxLegLength * 0.5f;
    private const float LowerLegLength = MaxLegLength * 0.5f;
    private const float LegThickness = 0.06f;
    private const float LegSpeed = 0.08f;
    private const float LiftHeight = 0.8f;
    private const float StepSize = 2.0f;
    private const float MoveThreshold = 1.0f;
    private const int StepCooldown = 5;
    private const int ForceStepInterval = 30;

    // --- Speed / combat (matching JS config) ---
    private const float IdleMaxSpeed = 0.04f;
    private const float ChaseMaxSpeed = 0.08f;
    private const float VelocityDamping = 0.9f;
    private const float DesiredVelocityScale = 0.02f;
    private const float CenterOfSupportBlend = 0.3f;
    private const float MaxFallSpeed = -0.5f;
    private const float MaxStepUpHeight = 1.5f;
    private const float SpiderDetectRange = 12f;
    private const float SpiderAttackRange = 3f;

    // --- Pathfinding ---
    private const float PathfindCooldownSec = 0.5f;
    private const float PathTargetMovedThreshold = 3f;
    private const float WaypointArrivalThreshold = 1.0f;

    // --- Idle movement ---
    private const float IdleLookAheadDist = 1.5f;
    private const int IdleObstacleMaxHeight = 2;
    private const float CliffDropThreshold = 3f;
    private const int IdleMinDirChange = 300;
    private const int IdleMaxDirChange = 600;

    // --- Stuck detection ---
    private const int StuckBufferSize = 30;
    private const float StuckThreshold = 0.5f;
    private const int AltDirectionDuration = 90;

    // --- Leg state arrays ---
    private readonly Vector3[] _hipOffsets;
    private readonly Vector3[] _restOffsets;
    private readonly Vector3[] _footPos;
    private readonly Vector3[] _footTarget;
    private readonly Vector3[] _footStart;
    private readonly bool[] _legMoving;
    private readonly float[] _legProgress;

    // Tripod gait: [0,3,4] and [1,2,5]
    private static readonly int[][] TripodGroups = { new[] { 0, 3, 4 }, new[] { 1, 2, 5 } };
    private int _activePair;
    private int _frameCount;
    private int _lastStepFrame;

    // Eye glow
    private float _eyePulsePhase;
    private Vector3 _eyeColor;

    // Stuck detection
    private readonly (float X, float Z)[] _posHistory = new (float, float)[StuckBufferSize];
    private int _posHistoryIdx;
    private int _posHistoryCount;
    private int _stuckCounter;
    private bool _isStuck;
    private Vector3 _altDirection;
    private int _altDirectionTimer;

    // A* pathfinding
    private List<Vector3>? _currentPath;
    private int _pathWaypointIndex;
    private float _lastPathfindTime;
    private float _elapsedTime;
    private Vector3? _pathTarget;

    // Idle wandering
    private Vector3 _idleWalkDirection;
    private int _idlePauseTimer;
    private int _idleNextDirectionChange;

    // Colors
    private readonly Vector3 _bodyColor;
    private readonly Vector3 _legColor;

    // Internal spider state
    private enum SpiderAIState { Idle, Chasing, Attacking }
    private SpiderAIState _spiderState = SpiderAIState.Idle;

    public SpiderMob(Vector3 position)
        : base(position, health: 8, speed: ChaseMaxSpeed)
    {
        BodyWidth = BodySize;
        BodyHeight = BodySize * 0.6f;
        AttackDamage = 2;
        AttackRange = SpiderAttackRange;
        DetectRange = SpiderDetectRange;

        IdleSoundName = "spider_idle";
        IdleSoundTimer = 5f + Random.Shared.NextSingle() * 10f;

        _bodyColor = new Vector3(0.25f, 0.18f, 0.12f);
        _legColor = new Vector3(0.15f, 0.1f, 0.08f);
        _eyeColor = new Vector3(2.0f, 0.05f, 0f);

        // Hip attachment points (C# convention: +X forward, +Z right)
        float bwHalf = BodySize * 0.3f; // 0.45
        float blHalf = BodySize * 0.5f; // 0.75

        _hipOffsets = new Vector3[]
        {
            new( blHalf * 0.8f, 0f, -bwHalf * 1.1f), // 0: Left Front
            new( blHalf * 0.8f, 0f,  bwHalf * 1.1f), // 1: Right Front
            new( 0f,            0f, -bwHalf * 1.2f),  // 2: Left Middle
            new( 0f,            0f,  bwHalf * 1.2f),  // 3: Right Middle
            new(-blHalf * 0.8f, 0f, -bwHalf * 1.1f), // 4: Left Back
            new(-blHalf * 0.8f, 0f,  bwHalf * 1.1f), // 5: Right Back
        };

        // Rest foot positions: hip offset extended outward * 1.2, slightly below body
        _restOffsets = new Vector3[LegCount];
        for (int i = 0; i < LegCount; i++)
        {
            var hip = _hipOffsets[i];
            _restOffsets[i] = new Vector3(hip.X * 2.2f, -0.3f, hip.Z * 2.2f);
        }

        _footPos = new Vector3[LegCount];
        _footTarget = new Vector3[LegCount];
        _footStart = new Vector3[LegCount];
        _legMoving = new bool[LegCount];
        _legProgress = new float[LegCount];

        // Random idle walk direction
        float rAngle = Random.Shared.NextSingle() * MathF.PI * 2f;
        _idleWalkDirection = new Vector3(MathF.Cos(rAngle), 0, MathF.Sin(rAngle));
        _idleNextDirectionChange = IdleMinDirChange + Random.Shared.Next(IdleMaxDirChange - IdleMinDirChange);

        InitializeFeet();
    }

    private void InitializeFeet()
    {
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);
        for (int i = 0; i < LegCount; i++)
        {
            var rest = _restOffsets[i];
            float wx = Position.X + rest.X * cosY - rest.Z * sinY;
            float wz = Position.Z + rest.X * sinY + rest.Z * cosY;
            float wy = Position.Y + rest.Y;
            _footPos[i] = new Vector3(wx, wy, wz);
            _footTarget[i] = _footPos[i];
            _footStart[i] = _footPos[i];
        }
    }

    // ========== OVERRIDE: bypass MobBase generic physics ==========

    protected override void ApplyGravity(float dt) { }
    protected override void ApplyMovement(float dt, WorldManager world) { }

    // ========== MAIN UPDATE ==========

    public override void Update(float dt, WorldManager world, Vector3 playerPos)
    {
        if (MarkedForRemoval) return;

        _frameCount++;
        _elapsedTime += dt;

        HurtTimer = MathF.Max(0, HurtTimer - dt);

        // Handle Hurt state (from MobBase.TakeDamage)
        if (State == MobState.Hurt)
        {
            StateTimer -= dt;
            // Apply knockback velocity (set by TakeDamage)
            ApplySpiderPhysics(world);
            if (StateTimer <= 0)
            {
                if (Health <= 0)
                {
                    DeathTimer = 0.8f;
                    TransitionTo(MobState.Death);
                }
                else
                {
                    _spiderState = SpiderAIState.Chasing;
                    TransitionTo(MobState.Chase);
                }
            }
            UpdateSpiderLegs(world);
            UpdateEyeGlow(dt);
            return;
        }

        // Handle Death state
        if (State == MobState.Death)
        {
            DeathTimer -= dt;
            Velocity.X = 0;
            Velocity.Z = 0;
            if (DeathTimer <= 0)
                MarkedForRemoval = true;
            return;
        }

        // Spider AI state machine
        UpdateSpiderState(playerPos);

        // Spider movement
        UpdateSpiderMovement(world, playerPos);

        // Spider physics (gravity, collision, step-up)
        ApplySpiderPhysics(world);

        // Tripod gait
        UpdateSpiderLegs(world);

        // Eye glow
        UpdateEyeGlow(dt);

        // Stuck detection (only during chase)
        if (_spiderState == SpiderAIState.Chasing)
            UpdateStuckDetection(world, playerPos);
        else
        {
            _isStuck = false;
            _stuckCounter = 0;
            _altDirectionTimer = 0;
            _posHistoryCount = 0;
            _posHistoryIdx = 0;
        }

        // Despawn check
        float distSq = (Position - playerPos).LengthSquared;
        if (distSq > DespawnRange * DespawnRange)
            MarkedForRemoval = true;
    }

    // ========== SPIDER STATE MACHINE ==========

    private void UpdateSpiderState(Vector3 playerPos)
    {
        float heightDiff = playerPos.Y - Position.Y;
        if (heightDiff > 3f)
        {
            _spiderState = SpiderAIState.Idle;
            TransitionTo(MobState.Idle);
            return;
        }

        float dx = playerPos.X - Position.X;
        float dz = playerPos.Z - Position.Z;
        float dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist <= SpiderAttackRange)
        {
            _spiderState = SpiderAIState.Attacking;
            TransitionTo(MobState.Attack);
        }
        else if (dist <= SpiderDetectRange)
        {
            _spiderState = SpiderAIState.Chasing;
            TransitionTo(MobState.Chase);
        }
        else
        {
            _spiderState = SpiderAIState.Idle;
            TransitionTo(MobState.Idle);
        }
    }

    // ========== SPIDER MOVEMENT ==========

    private void UpdateSpiderMovement(WorldManager world, Vector3 playerPos)
    {
        // Compute center of leg support
        Vector3 centerOfSupport = Vector3.Zero;
        for (int i = 0; i < LegCount; i++)
            centerOfSupport += _footPos[i];
        centerOfSupport /= LegCount;

        // Determine AI target position based on state
        Vector3 targetPos;
        float maxSpeed;

        switch (_spiderState)
        {
            case SpiderAIState.Chasing:
                targetPos = GetChaseTarget(world, playerPos);
                maxSpeed = ChaseMaxSpeed;
                break;

            case SpiderAIState.Attacking:
                // Circle around player at attack radius
                Vector3 attackDir = new(playerPos.X - Position.X, 0, playerPos.Z - Position.Z);
                float attackDirLen = attackDir.Length;
                if (attackDirLen > 0.001f) attackDir /= attackDirLen;
                targetPos = Position + attackDir * 2f;
                maxSpeed = IdleMaxSpeed;
                break;

            default: // Idle
                UpdateIdleMovement(world);
                targetPos = new Vector3(
                    Position.X + _idleWalkDirection.X * 3f,
                    Position.Y,
                    Position.Z + _idleWalkDirection.Z * 3f);
                maxSpeed = IdleMaxSpeed;
                break;
        }

        // Rotate toward movement direction
        Vector3 toTarget = new(targetPos.X - Position.X, 0, targetPos.Z - Position.Z);
        float toTargetLen = toTarget.Length;
        if (toTargetLen > 0.01f)
        {
            float targetAngle = MathF.Atan2(toTarget.Z, toTarget.X);
            float turnRate = _spiderState == SpiderAIState.Chasing ? 5f : 3f;
            // Use frame-independent turning: approximate per-frame factor
            Yaw = LerpAngle(Yaw, targetAngle, MathF.Min(turnRate * 0.016f, 1f));
        }

        // Blend center-of-support with AI target (30% legs, 70% AI)
        Vector3 blendedTarget = new(
            centerOfSupport.X * CenterOfSupportBlend + targetPos.X * (1f - CenterOfSupportBlend),
            Position.Y,
            centerOfSupport.Z * CenterOfSupportBlend + targetPos.Z * (1f - CenterOfSupportBlend));

        // Desired velocity
        float desiredVX = (blendedTarget.X - Position.X) * DesiredVelocityScale;
        float desiredVZ = (blendedTarget.Z - Position.Z) * DesiredVelocityScale;

        // Apply damping: velocity = velocity * 0.9 + desired (NOT 0.9*old + 0.1*new!)
        Velocity.X = Velocity.X * VelocityDamping + desiredVX;
        Velocity.Z = Velocity.Z * VelocityDamping + desiredVZ;

        // Clamp speed
        float currentSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
        if (currentSpeed > maxSpeed)
        {
            Velocity.X = Velocity.X / currentSpeed * maxSpeed;
            Velocity.Z = Velocity.Z / currentSpeed * maxSpeed;
        }
    }

    // ========== IDLE MOVEMENT (environmental awareness) ==========

    private void UpdateIdleMovement(WorldManager world)
    {
        _idlePauseTimer++;
        bool shouldTurn = false;

        float checkX = Position.X + _idleWalkDirection.X * IdleLookAheadDist;
        float checkZ = Position.Z + _idleWalkDirection.Z * IdleLookAheadDist;
        int checkY = (int)MathF.Floor(Position.Y);
        int aheadBX = (int)MathF.Floor(checkX);
        int aheadBZ = (int)MathF.Floor(checkZ);

        // Check obstacle height ahead
        int obstacleHeight = 0;
        for (int h = 0; h < 4; h++)
        {
            byte block = world.GetBlockAt(aheadBX, checkY + h, aheadBZ);
            if (!BlockRegistry.IsPassable(block))
                obstacleHeight++;
            else
                break;
        }
        if (obstacleHeight > IdleObstacleMaxHeight) shouldTurn = true;

        // Check for water ahead (block 9)
        if (!shouldTurn)
        {
            for (int offset = -2; offset <= 0; offset++)
            {
                byte block = world.GetBlockAt(aheadBX, checkY + offset, aheadBZ);
                if (block == 9) { shouldTurn = true; break; }
            }
        }

        // Check for cliff (drop > 3 blocks)
        if (!shouldTurn)
        {
            float aheadGround = FindGroundLevel(checkX, checkZ, world);
            float currentGround = FindGroundLevel(Position.X, Position.Z, world);
            float drop = currentGround - aheadGround;
            if (drop > CliffDropThreshold) shouldTurn = true;
        }

        // Turn if danger or periodic direction change
        if (shouldTurn || _idlePauseTimer > _idleNextDirectionChange)
        {
            float turnAngle;
            if (shouldTurn)
                turnAngle = (Random.Shared.NextSingle() < 0.5f ? 1 : -1)
                    * (MathF.PI * 0.5f + Random.Shared.NextSingle() * MathF.PI * 0.5f);
            else
                turnAngle = (Random.Shared.NextSingle() - 0.5f) * MathF.PI * 1.5f;

            float cos = MathF.Cos(turnAngle), sin = MathF.Sin(turnAngle);
            float newX = _idleWalkDirection.X * cos - _idleWalkDirection.Z * sin;
            float newZ = _idleWalkDirection.X * sin + _idleWalkDirection.Z * cos;
            float len = MathF.Sqrt(newX * newX + newZ * newZ);
            if (len > 0.001f)
                _idleWalkDirection = new Vector3(newX / len, 0, newZ / len);

            _idlePauseTimer = 0;
            _idleNextDirectionChange = IdleMinDirChange + Random.Shared.Next(IdleMaxDirChange - IdleMinDirChange);
        }
    }

    // ========== CHASE WITH A* PATHFINDING ==========

    private Vector3 GetChaseTarget(WorldManager world, Vector3 playerPos)
    {
        // Recalculate path if cooldown elapsed or target moved significantly
        bool targetMoved = !_pathTarget.HasValue ||
            MathF.Abs(playerPos.X - _pathTarget.Value.X) > PathTargetMovedThreshold ||
            MathF.Abs(playerPos.Z - _pathTarget.Value.Z) > PathTargetMovedThreshold;

        if (_elapsedTime - _lastPathfindTime > PathfindCooldownSec || targetMoved || _currentPath == null)
        {
            _lastPathfindTime = _elapsedTime;
            _pathTarget = playerPos;

            var path = Pathfinding.FindPath(Position, playerPos, world, new Pathfinding.PathfindOptions
            {
                MobHeight = 2,
                MaxStepUp = 2,
                MaxStepDown = 4,
                MaxIterations = 300,
                MaxPathLength = 30
            });

            if (path != null && path.Count > 1)
            {
                _currentPath = Pathfinding.SmoothPath(path, world, 2);
                _pathWaypointIndex = 1;
            }
            else
            {
                _currentPath = null;
            }
        }

        // Follow path waypoints
        if (_currentPath != null && _pathWaypointIndex < _currentPath.Count)
        {
            int nextIdx = Pathfinding.GetNextWaypoint(_currentPath, Position, _pathWaypointIndex, WaypointArrivalThreshold);
            if (nextIdx < _currentPath.Count)
            {
                _pathWaypointIndex = nextIdx;
                return _currentPath[nextIdx];
            }
            _currentPath = null;
        }

        // Fallback: direct to player (or stuck alternative)
        if (_isStuck && _altDirectionTimer > 0)
            return Position + _altDirection * 5f;

        return new Vector3(playerPos.X, Position.Y, playerPos.Z);
    }

    // ========== SPIDER PHYSICS ==========

    private void ApplySpiderPhysics(WorldManager world)
    {
        // Gravity (per frame)
        Velocity.Y -= GameConfig.GRAVITY;

        // Spiders CANNOT jump
        if (Velocity.Y > 0 && State != MobState.Hurt)
            Velocity.Y = 0;

        // Cap fall speed
        if (Velocity.Y < MaxFallSpeed)
            Velocity.Y = MaxFallSpeed;

        float newX = Position.X + Velocity.X;
        float newZ = Position.Z + Velocity.Z;
        float newY = Position.Y + Velocity.Y;

        // Find ground at new horizontal position
        float groundLevel = FindGroundLevel(newX, newZ, world);
        float targetGroundY = groundLevel + BodyYOffset;
        float heightDiff = targetGroundY - Position.Y;

        bool onGround = MathF.Abs(Velocity.Y) < 0.02f;

        // Cliff detection: walking off edge while on ground
        if (heightDiff < -2.0f && onGround && !CheckSpiderCollision(world, newX, newY, newZ))
        {
            Position.X = newX;
            Position.Z = newZ;
            Position.Y = newY;
        }
        // Landing (falling onto ground)
        else if (newY <= targetGroundY && Velocity.Y < 0)
        {
            if (heightDiff > MaxStepUpHeight)
            {
                // Wall too high to step up - don't move horizontally, just land
                Position.Y = MathF.Max(Position.Y, targetGroundY);
                Velocity.Y = 0;
                Velocity.X *= 0.5f;
                Velocity.Z *= 0.5f;
            }
            else
            {
                // Normal landing or step-up
                Position.X = newX;
                Position.Z = newZ;
                Position.Y = targetGroundY;
                Velocity.Y = 0;
            }
        }
        // In air, no collision
        else if (!CheckSpiderCollision(world, newX, newY, newZ))
        {
            Position.X = newX;
            Position.Z = newZ;
            Position.Y = newY;
        }
        // Blocked
        else
        {
            if (MathF.Abs(heightDiff) <= MaxStepUpHeight)
            {
                Position.X = newX;
                Position.Z = newZ;
                Position.Y = targetGroundY;
                Velocity.Y = 0;
            }
            else
            {
                // Can't move - zero horizontal velocity
                Velocity.X = 0;
                Velocity.Z = 0;
                Velocity.Y = 0;
            }
        }

        // Safety: clamp to world bounds
        Position.Y = MathF.Max(Position.Y, BodyHH + 1f);
    }

    private float FindGroundLevel(float x, float z, WorldManager world)
    {
        int bx = (int)MathF.Floor(x);
        int bz = (int)MathF.Floor(z);
        int startY = (int)MathF.Floor(Position.Y + 10);
        int endY = Math.Max(1, (int)MathF.Floor(Position.Y - 15));

        for (int y = startY; y >= endY; y--)
        {
            byte block = world.GetBlockAt(bx, y, bz);
            if (!BlockRegistry.IsPassable(block))
            {
                byte above = world.GetBlockAt(bx, y + 1, bz);
                if (BlockRegistry.IsPassable(above))
                    return y + 1f;
            }
        }
        return Position.Y - BodyHH;
    }

    private bool CheckSpiderCollision(WorldManager world, float x, float y, float z)
    {
        float halfSize = BodySize * 0.5f;
        Span<(float px, float py, float pz)> points = stackalloc (float, float, float)[]
        {
            (x - halfSize, y, z - halfSize),
            (x + halfSize, y, z - halfSize),
            (x - halfSize, y, z + halfSize),
            (x + halfSize, y, z + halfSize),
            (x, y + halfSize, z),
            (x, y - halfSize, z)
        };

        foreach (var (px, py, pz) in points)
        {
            byte block = world.GetBlockAt((int)MathF.Floor(px), (int)MathF.Floor(py), (int)MathF.Floor(pz));
            if (!BlockRegistry.IsPassable(block))
                return true;
        }
        return false;
    }

    // ========== EYE GLOW ==========

    private void UpdateEyeGlow(float dt)
    {
        _eyePulsePhase += dt * 3.0f;
        float pulse = 0.7f + MathF.Sin(_eyePulsePhase) * 0.3f;

        float baseIntensity = _spiderState switch
        {
            SpiderAIState.Chasing => 1.5f,
            SpiderAIState.Attacking => 2.0f,
            _ => 1.0f
        };

        float intensity = baseIntensity * pulse;
        _eyeColor = new Vector3(intensity * 2.5f, intensity * 0.05f, 0f);
    }

    // ========== TRIPOD GAIT ==========

    private void UpdateSpiderLegs(WorldManager world)
    {
        bool anyMoving = false;
        for (int i = 0; i < LegCount; i++)
        {
            if (!_legMoving[i]) continue;
            anyMoving = true;

            _legProgress[i] += LegSpeed;
            if (_legProgress[i] >= 1.0f)
            {
                _legProgress[i] = 1.0f;
                _legMoving[i] = false;
                _footPos[i] = _footTarget[i];
                _lastStepFrame = _frameCount;
            }
            else
            {
                _footPos[i] = Vector3.Lerp(_footStart[i], _footTarget[i], _legProgress[i]);
                _footPos[i].Y += LiftHeight * MathF.Sin(MathF.PI * _legProgress[i]);
            }
        }

        if (!anyMoving && ShouldStep())
            StartNextStep(world);
    }

    private bool ShouldStep()
    {
        int framesSinceStep = _frameCount - _lastStepFrame;
        if (framesSinceStep < StepCooldown) return false;

        Vector3 feetCenter = Vector3.Zero;
        for (int i = 0; i < LegCount; i++)
            feetCenter += _footPos[i];
        feetCenter /= LegCount;

        float dx = Position.X - feetCenter.X;
        float dz = Position.Z - feetCenter.Z;
        float drift = MathF.Sqrt(dx * dx + dz * dz);

        return drift > MoveThreshold || framesSinceStep > ForceStepInterval;
    }

    private void StartNextStep(WorldManager world)
    {
        _lastStepFrame = _frameCount;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        // Step direction from velocity, or forward if stationary
        float vLen = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
        Vector3 stepDir;
        if (vLen > 0.001f)
            stepDir = new Vector3(Velocity.X / vLen, 0, Velocity.Z / vLen);
        else
            stepDir = new Vector3(cosY, 0, sinY);

        var group = TripodGroups[_activePair];
        foreach (int legIdx in group)
        {
            var rest = _restOffsets[legIdx];
            // Rest position in world space
            float rx = Position.X + rest.X * cosY - rest.Z * sinY;
            float rz = Position.Z + rest.X * sinY + rest.Z * cosY;

            // Offset by step direction
            float tx = rx + stepDir.X * StepSize;
            float tz = rz + stepDir.Z * StepSize;

            // Constrain to max leg length from hip
            var hip = _hipOffsets[legIdx];
            float hipWX = Position.X + hip.X * cosY - hip.Z * sinY;
            float hipWZ = Position.Z + hip.X * sinY + hip.Z * cosY;
            float legDX = tx - hipWX;
            float legDZ = tz - hipWZ;
            float legDist = MathF.Sqrt(legDX * legDX + legDZ * legDZ);
            if (legDist > MaxLegLength)
            {
                tx = hipWX + legDX / legDist * MaxLegLength;
                tz = hipWZ + legDZ / legDist * MaxLegLength;
            }

            // Find ground at target
            float ty = FindGroundLevel(tx, tz, world);

            // Clamp foot Y to valid range
            float maxFootDepth = Position.Y - BodySize;
            float minFootHeight = Position.Y - MaxLegLength;
            ty = MathF.Max(minFootHeight, MathF.Min(maxFootDepth, ty));

            _footStart[legIdx] = _footPos[legIdx];
            _footTarget[legIdx] = new Vector3(tx, ty, tz);
            _legMoving[legIdx] = true;
            _legProgress[legIdx] = 0f;
        }

        _activePair = (_activePair + 1) % 2;
    }

    // ========== STUCK DETECTION ==========

    private void UpdateStuckDetection(WorldManager world, Vector3 playerPos)
    {
        _posHistory[_posHistoryIdx] = (Position.X, Position.Z);
        _posHistoryIdx = (_posHistoryIdx + 1) % StuckBufferSize;
        if (_posHistoryCount < StuckBufferSize) _posHistoryCount++;

        if (_posHistoryCount >= StuckBufferSize && _frameCount % StuckBufferSize == 0)
        {
            int oldest = _posHistoryIdx;
            int newest = (_posHistoryIdx - 1 + StuckBufferSize) % StuckBufferSize;

            float dx = _posHistory[newest].X - _posHistory[oldest].X;
            float dz = _posHistory[newest].Z - _posHistory[oldest].Z;
            float movedDist = MathF.Sqrt(dx * dx + dz * dz);

            if (movedDist < StuckThreshold)
            {
                _stuckCounter++;
                if (_stuckCounter >= 2)
                {
                    _isStuck = true;
                    _altDirection = GenerateAlternativeDirection(playerPos, world);
                    _altDirectionTimer = AltDirectionDuration;
                }
            }
            else
            {
                _stuckCounter = 0;
                _isStuck = false;
            }
        }

        if (_altDirectionTimer > 0)
            _altDirectionTimer--;
        else
            _isStuck = false;
    }

    private Vector3 GenerateAlternativeDirection(Vector3 playerPos, WorldManager world)
    {
        Vector3 toPlayer = new(playerPos.X - Position.X, 0, playerPos.Z - Position.Z);
        float toPlayerLen = toPlayer.Length;
        if (toPlayerLen > 0.001f) toPlayer /= toPlayerLen;
        else toPlayer = new Vector3(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));

        Vector3 perpL = new(-toPlayer.Z, 0, toPlayer.X);
        Vector3 perpR = new(toPlayer.Z, 0, -toPlayer.X);

        // Normalize safely
        static Vector3 SafeNormalize(Vector3 v)
        {
            float len = v.Length;
            return len > 0.001f ? v / len : Vector3.UnitX;
        }

        Vector3 diagFL = SafeNormalize(toPlayer + perpL);
        Vector3 diagFR = SafeNormalize(toPlayer + perpR);
        Vector3 backL = SafeNormalize(-toPlayer + perpL);
        Vector3 backR = SafeNormalize(-toPlayer + perpR);

        Span<Vector3> candidates = stackalloc Vector3[] { perpL, perpR, diagFL, diagFR, backL, backR };

        float bestScore = float.MinValue;
        Vector3 bestDir = perpL;

        foreach (var dir in candidates)
        {
            float score = 0f;
            bool blocked = false;

            for (int step = 1; step <= 3; step++)
            {
                int bx = (int)MathF.Floor(Position.X + dir.X * step);
                int by = (int)MathF.Floor(Position.Y);
                int bz = (int)MathF.Floor(Position.Z + dir.Z * step);

                if (!BlockRegistry.IsPassable(world.GetBlockAt(bx, by, bz)))
                {
                    blocked = true;
                    break;
                }
            }

            score += blocked ? -1000f : 100f;

            // Favor directions toward player
            float heightCheck = FindGroundLevel(
                Position.X + dir.X * 3f, Position.Z + dir.Z * 3f, world);
            float heightDiff = heightCheck - FindGroundLevel(Position.X, Position.Z, world);

            if (heightDiff > 0) score -= heightDiff * 20f;
            else score -= MathF.Abs(heightDiff) * 5f;

            score += Vector3.Dot(dir, toPlayer) * 20f;

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        return bestDir;
    }

    // ========== INVERSE KINEMATICS ==========

    private Vector3 CalculateKneePosition(Vector3 hipPos, Vector3 footPos, int legIndex)
    {
        Vector3 hipToFoot = footPos - hipPos;
        float dist = hipToFoot.Length;

        if (dist < 0.001f)
            return hipPos + new Vector3(0, UpperLegLength * 0.7f, 0);

        // Clamp to prevent over-extension
        float maxDist = UpperLegLength + LowerLegLength - 0.01f;
        if (dist > maxDist)
        {
            hipToFoot = hipToFoot / dist * maxDist;
            dist = maxDist;
        }

        // Law of cosines: angle at hip
        float cosAlpha = (dist * dist + UpperLegLength * UpperLegLength - LowerLegLength * LowerLegLength)
                         / (2f * dist * UpperLegLength);
        cosAlpha = Math.Clamp(cosAlpha, -1f, 1f);
        float alpha = MathF.Acos(cosAlpha);

        // Outward bias: push knee up and outward from body
        float cosY = MathF.Cos(Yaw), sinY = MathF.Sin(Yaw);
        var hipOff = _hipOffsets[legIndex];
        Vector3 outwardLocal = new(hipOff.X, 1.0f, hipOff.Z);
        float outLen = outwardLocal.Length;
        if (outLen > 0.001f) outwardLocal /= outLen;
        else outwardLocal = Vector3.UnitY;

        // Transform outward bias to world space
        Vector3 outwardWorld = new(
            outwardLocal.X * cosY - outwardLocal.Z * sinY,
            outwardLocal.Y,
            outwardLocal.X * sinY + outwardLocal.Z * cosY);

        // Rotation axis perpendicular to hip-to-foot and outward bias
        Vector3 hipDir = hipToFoot / dist;
        Vector3 rotAxis = Vector3.Cross(hipDir, outwardWorld);
        float rotAxisLen = rotAxis.Length;

        if (rotAxisLen < 0.01f)
        {
            rotAxis = Vector3.Cross(hipDir, Vector3.UnitY);
            rotAxisLen = rotAxis.Length;
            if (rotAxisLen < 0.01f)
            {
                rotAxis = Vector3.Cross(hipDir, Vector3.UnitX);
                rotAxisLen = rotAxis.Length;
            }
        }
        rotAxis /= rotAxisLen;

        // Rodrigues' rotation
        Vector3 rotated = RotateAroundAxis(hipDir, rotAxis, alpha);
        return hipPos + rotated * UpperLegLength;
    }

    private static Vector3 RotateAroundAxis(Vector3 v, Vector3 axis, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return v * cos + Vector3.Cross(axis, v) * sin + axis * Vector3.Dot(axis, v) * (1f - cos);
    }

    // ========== MESH BUILDING ==========

    public override MobMeshData BuildMesh(float skyMultiplier, WorldManager world)
    {
        // Body: 36, Eyes: 4*6*6=144, Legs: 6*2*4*6=288 → Total: 468
        const int bodyVerts = 36;
        const int eyeVerts = 4 * 6 * 6;
        const int legVerts = LegCount * 2 * 4 * 6;
        const int totalVerts = bodyVerts + eyeVerts + legVerts;

        var verts = new float[totalVerts * ChunkMesh.FloatsPerVertex];
        int idx = 0;

        Vector3 color = HurtTimer > 0 ? new Vector3(1f, 0.3f, 0.3f) : _bodyColor;
        Vector3 legCol = HurtTimer > 0 ? new Vector3(1f, 0.4f, 0.4f) : _legColor;

        float deathScale = 1f;
        if (State == MobState.Death)
            deathScale = MathF.Max(0.01f, DeathTimer / 0.8f);

        var (skyBri, blockBri) = GetLighting(world);
        var (u0, v0, u1, v1) = TextureAtlas.GetFaceUVs(3, 0);

        float cx = Position.X, cy = Position.Y, cz = Position.Z;
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        // === BODY ===
        float bw = BodyHW * deathScale;
        float bh = BodyHH * deathScale;
        float bl = BodyHL * deathScale;

        Span<Vector3> bc = stackalloc Vector3[8];
        bc[0] = Rot(-bw, -bh, -bl, cosY, sinY, cx, cy, cz);
        bc[1] = Rot( bw, -bh, -bl, cosY, sinY, cx, cy, cz);
        bc[2] = Rot( bw, -bh,  bl, cosY, sinY, cx, cy, cz);
        bc[3] = Rot(-bw, -bh,  bl, cosY, sinY, cx, cy, cz);
        bc[4] = Rot(-bw,  bh, -bl, cosY, sinY, cx, cy, cz);
        bc[5] = Rot( bw,  bh, -bl, cosY, sinY, cx, cy, cz);
        bc[6] = Rot( bw,  bh,  bl, cosY, sinY, cx, cy, cz);
        bc[7] = Rot(-bw,  bh,  bl, cosY, sinY, cx, cy, cz);

        AddFace(verts, ref idx, bc[7], bc[6], bc[5], bc[4], new Vector3(0, 1, 0), color, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, bc[0], bc[1], bc[2], bc[3], new Vector3(0, -1, 0), color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, bc[3], bc[2], bc[6], bc[7], new Vector3(0, 0, 1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, bc[1], bc[0], bc[4], bc[5], new Vector3(0, 0, -1), color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, bc[2], bc[1], bc[5], bc[6], new Vector3(1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, bc[0], bc[3], bc[7], bc[4], new Vector3(-1, 0, 0), color * 0.9f, u0, v0, u1, v1, skyBri, blockBri);

        // === EYES (4 small cubes on the +X front face) ===
        float eyeMain = 0.07f * deathScale;
        float eyeSmall = 0.05f * deathScale;

        BuildEyeCube(verts, ref idx, BodyHW * 0.95f * deathScale, BodyHH * 0.3f * deathScale, -BodyHL * 0.15f * deathScale, eyeMain,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);
        BuildEyeCube(verts, ref idx, BodyHW * 0.95f * deathScale, BodyHH * 0.3f * deathScale, BodyHL * 0.15f * deathScale, eyeMain,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);
        BuildEyeCube(verts, ref idx, BodyHW * 0.92f * deathScale, BodyHH * 0.15f * deathScale, -BodyHL * 0.35f * deathScale, eyeSmall,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);
        BuildEyeCube(verts, ref idx, BodyHW * 0.92f * deathScale, BodyHH * 0.15f * deathScale, BodyHL * 0.35f * deathScale, eyeSmall,
            cosY, sinY, cx, cy, cz, u0, v0, u1, v1);

        // === LEGS (6 legs, IK-driven) ===
        for (int i = 0; i < LegCount; i++)
        {
            var hip = _hipOffsets[i];
            Vector3 hipWorld = new(
                cx + (hip.X * cosY - hip.Z * sinY) * deathScale,
                cy + hip.Y * deathScale,
                cz + (hip.X * sinY + hip.Z * cosY) * deathScale);

            Vector3 foot = _footPos[i];
            Vector3 knee;

            if (deathScale < 0.5f)
            {
                float t = 1f - deathScale * 2f;
                knee = Vector3.Lerp(hipWorld + new Vector3(0, 0.3f * deathScale, 0), new Vector3(cx, cy, cz), t);
                foot = Vector3.Lerp(_footPos[i], new Vector3(cx, cy - 0.1f, cz), t);
            }
            else
            {
                if (deathScale < 1f)
                    foot = Vector3.Lerp(new Vector3(cx, cy, cz), _footPos[i], deathScale);

                knee = CalculateKneePosition(hipWorld, foot, i);
            }

            float thick = LegThickness * deathScale;

            BuildLegSegment(verts, ref idx, hipWorld, knee, thick, legCol, u0, v0, u1, v1, skyBri, blockBri);
            BuildLegSegment(verts, ref idx, knee, foot, thick * 0.85f, legCol, u0, v0, u1, v1, skyBri, blockBri);
        }

        return new MobMeshData(verts, totalVerts);
    }

    // ========== MESH HELPERS ==========

    private void BuildEyeCube(float[] verts, ref int idx,
        float lx, float ly, float lz, float halfSize,
        float cosY, float sinY, float cx, float cy, float cz,
        float u0, float v0, float u1, float v1)
    {
        float s = halfSize;
        Vector3 eyeCol = _eyeColor;

        Span<Vector3> ec = stackalloc Vector3[8];
        ec[0] = Rot(lx - s, ly - s, lz - s, cosY, sinY, cx, cy, cz);
        ec[1] = Rot(lx + s, ly - s, lz - s, cosY, sinY, cx, cy, cz);
        ec[2] = Rot(lx + s, ly - s, lz + s, cosY, sinY, cx, cy, cz);
        ec[3] = Rot(lx - s, ly - s, lz + s, cosY, sinY, cx, cy, cz);
        ec[4] = Rot(lx - s, ly + s, lz - s, cosY, sinY, cx, cy, cz);
        ec[5] = Rot(lx + s, ly + s, lz - s, cosY, sinY, cx, cy, cz);
        ec[6] = Rot(lx + s, ly + s, lz + s, cosY, sinY, cx, cy, cz);
        ec[7] = Rot(lx - s, ly + s, lz + s, cosY, sinY, cx, cy, cz);

        AddFace(verts, ref idx, ec[7], ec[6], ec[5], ec[4], new Vector3(0, 1, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[0], ec[1], ec[2], ec[3], new Vector3(0, -1, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[3], ec[2], ec[6], ec[7], new Vector3(0, 0, 1), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[1], ec[0], ec[4], ec[5], new Vector3(0, 0, -1), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[2], ec[1], ec[5], ec[6], new Vector3(1, 0, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
        AddFace(verts, ref idx, ec[0], ec[3], ec[7], ec[4], new Vector3(-1, 0, 0), eyeCol, u0, v0, u1, v1, 1f, 1f);
    }

    private static void BuildLegSegment(float[] verts, ref int idx,
        Vector3 start, Vector3 end, float thickness,
        Vector3 color, float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        Vector3 dir = end - start;
        float len = dir.Length;

        if (len < 0.001f)
        {
            for (int f = 0; f < 4; f++)
                AddFace(verts, ref idx, start, start, start, start,
                    Vector3.UnitY, color, u0, v0, u1, v1, skyBri, blockBri);
            return;
        }

        dir /= len;

        Vector3 right;
        if (MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.99f)
            right = Vector3.Cross(dir, Vector3.UnitX);
        else
            right = Vector3.Cross(dir, Vector3.UnitY);

        float rightLen = right.Length;
        if (rightLen < 0.001f)
        {
            for (int f = 0; f < 4; f++)
                AddFace(verts, ref idx, start, start, start, start,
                    Vector3.UnitY, color, u0, v0, u1, v1, skyBri, blockBri);
            return;
        }

        right = right / rightLen * thickness;
        Vector3 up = Vector3.Cross(Vector3.Normalize(right), dir) * thickness;

        Vector3 s0 = start - right - up;
        Vector3 s1 = start + right - up;
        Vector3 s2 = start + right + up;
        Vector3 s3 = start - right + up;
        Vector3 e0 = end - right - up;
        Vector3 e1 = end + right - up;
        Vector3 e2 = end + right + up;
        Vector3 e3 = end - right + up;

        Vector3 nUp = Vector3.Normalize(up);
        Vector3 nRight = Vector3.Normalize(right);

        AddFace(verts, ref idx, s3, s2, e2, e3, nUp, color, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, s1, s0, e0, e1, -nUp, color * 0.7f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, s2, s1, e1, e2, nRight, color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
        AddFace(verts, ref idx, s0, s3, e3, e0, -nRight, color * 0.85f, u0, v0, u1, v1, skyBri, blockBri);
    }

    // ========== VERTEX HELPERS ==========

    private static Vector3 Rot(float lx, float ly, float lz,
        float cosY, float sinY, float cx, float cy, float cz)
    {
        float rx = lx * cosY - lz * sinY;
        float rz = lx * sinY + lz * cosY;
        return new Vector3(cx + rx, cy + ly, cz + rz);
    }

    private static void AddFace(float[] verts, ref int idx,
        Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
        Vector3 normal, Vector3 color,
        float u0, float v0, float u1, float v1,
        float skyBri, float blockBri)
    {
        AddVertex(verts, ref idx, bl, normal, color, u0, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, br, normal, color, u1, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, tr, normal, color, u1, v1, skyBri, blockBri);
        AddVertex(verts, ref idx, bl, normal, color, u0, v0, skyBri, blockBri);
        AddVertex(verts, ref idx, tr, normal, color, u1, v1, skyBri, blockBri);
        AddVertex(verts, ref idx, tl, normal, color, u0, v1, skyBri, blockBri);
    }

    private static void AddVertex(float[] verts, ref int idx,
        Vector3 pos, Vector3 normal, Vector3 color,
        float u, float v, float skyBri, float blockBri)
    {
        verts[idx++] = pos.X;
        verts[idx++] = pos.Y;
        verts[idx++] = pos.Z;
        verts[idx++] = normal.X;
        verts[idx++] = normal.Y;
        verts[idx++] = normal.Z;
        verts[idx++] = color.X;
        verts[idx++] = color.Y;
        verts[idx++] = color.Z;
        verts[idx++] = u;
        verts[idx++] = v;
        verts[idx++] = skyBri;
        verts[idx++] = blockBri;
        verts[idx++] = 1.0f; // AO (no occlusion for mobs)
    }
}
