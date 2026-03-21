using OpenTK.Mathematics;
using CSharpWebcraft.Core;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Player;

public class PlayerPhysics
{
    private readonly Camera _camera;
    private readonly WorldManager _world;

    public float VelocityY;
    public bool IsOnGround;
    public bool IsUnderwater;
    public bool IsSteppingUp;
    private float _stepUpTarget;
    private float _stepUpProgress;
    private float _swimStrokeCooldown;

    // Health & oxygen
    public int Health { get; set; } = GameConfig.PLAYER_MAX_HEALTH;
    public int MaxHealth => GameConfig.PLAYER_MAX_HEALTH;
    public float Oxygen { get; set; } = GameConfig.OXYGEN_MAX;
    private float _oxygenDamageTimer;

    // Pre-calculated collision offsets
    private static readonly float[] CheckYOffsets = { -GameConfig.PLAYER_HEIGHT * 0.45f, 0, GameConfig.PLAYER_HEIGHT * 0.45f };
    private static readonly (float dx, float dz)[] CheckXZOffsets = {
        (0, 0), (GameConfig.PLAYER_RADIUS, 0), (-GameConfig.PLAYER_RADIUS, 0),
        (0, GameConfig.PLAYER_RADIUS), (0, -GameConfig.PLAYER_RADIUS),
        (GameConfig.PLAYER_RADIUS, GameConfig.PLAYER_RADIUS), (-GameConfig.PLAYER_RADIUS, -GameConfig.PLAYER_RADIUS),
        (GameConfig.PLAYER_RADIUS, -GameConfig.PLAYER_RADIUS), (-GameConfig.PLAYER_RADIUS, GameConfig.PLAYER_RADIUS)
    };

    public PlayerPhysics(Camera camera, WorldManager world)
    {
        _camera = camera;
        _world = world;
    }

    public void Update(float deltaTime)
    {
        if (_swimStrokeCooldown > 0) _swimStrokeCooldown -= deltaTime;

        // Check underwater
        int headX = (int)MathF.Floor(_camera.Position.X);
        int headY = (int)MathF.Floor(_camera.Position.Y);
        int headZ = (int)MathF.Floor(_camera.Position.Z);
        IsUnderwater = _world.GetBlockAt(headX, headY, headZ) == 9;

        // Step up handling
        if (IsSteppingUp)
        {
            _stepUpProgress += GameConfig.STEP_UP_SPEED * deltaTime;
            if (_stepUpProgress >= 1f)
            {
                _camera.Position.Y = _stepUpTarget;
                IsSteppingUp = false;
                IsOnGround = true;
            }
            else
            {
                _camera.Position.Y += (_stepUpTarget - _camera.Position.Y) * deltaTime * GameConfig.STEP_UP_SPEED;
                IsOnGround = true;
                VelocityY = 0;
            }
            return;
        }

        // Gravity
        if (IsUnderwater)
        {
            VelocityY -= GameConfig.WATER_GRAVITY;
            VelocityY += GameConfig.WATER_BUOYANCY;
            VelocityY = Math.Clamp(VelocityY, -0.3f, 0.3f);
        }
        else
        {
            VelocityY -= GameConfig.GRAVITY;
            VelocityY = MathF.Max(VelocityY, -GameConfig.GRAVITY * 20);
        }

        float feetOffset = GameConfig.PLAYER_HEIGHT * 0.5f;
        float headOffset = GameConfig.PLAYER_HEIGHT * 0.45f;
        float newY = _camera.Position.Y + VelocityY;

        if (VelocityY <= 0 && CheckCollision(_camera.Position.X, newY - feetOffset, _camera.Position.Z))
        {
            VelocityY = 0;
            IsOnGround = true;
        }
        else if (VelocityY > 0 && CheckCollision(_camera.Position.X, newY + headOffset, _camera.Position.Z))
        {
            float ceilY = MathF.Floor(newY + headOffset);
            _camera.Position.Y = ceilY - headOffset - 0.01f;
            VelocityY = 0;
            IsOnGround = false;
        }
        else
        {
            _camera.Position.Y = newY;
            IsOnGround = false;
        }

        // Oxygen
        if (IsUnderwater)
        {
            Oxygen -= GameConfig.OXYGEN_DEPLETION_RATE * deltaTime;
            if (Oxygen <= 0)
            {
                Oxygen = 0;
                _oxygenDamageTimer += deltaTime * 1000f;
                if (_oxygenDamageTimer >= GameConfig.OXYGEN_DAMAGE_INTERVAL)
                {
                    Health = Math.Max(0, Health - GameConfig.OXYGEN_DAMAGE);
                    _oxygenDamageTimer = 0;
                }
            }
        }
        else
        {
            Oxygen = MathF.Min(Oxygen + GameConfig.OXYGEN_REGENERATION_RATE * deltaTime, GameConfig.OXYGEN_MAX);
            _oxygenDamageTimer = 0;
        }
    }

    public bool CheckCollision(float x, float y, float z)
    {
        for (int yIdx = 0; yIdx < 3; yIdx++)
        {
            float cY = y + CheckYOffsets[yIdx];
            for (int xzIdx = 0; xzIdx < 9; xzIdx++)
            {
                float pX = x + CheckXZOffsets[xzIdx].dx;
                float pZ = z + CheckXZOffsets[xzIdx].dz;

                int wx = (int)MathF.Floor(pX);
                int wy = (int)MathF.Floor(cY);
                int wz = (int)MathF.Floor(pZ);
                byte blkType = _world.GetBlockAt(wx, wy, wz);

                if (!BlockRegistry.IsPassable(blkType))
                {
                    if (pX >= wx && pX < wx + 1 && cY >= wy && cY < wy + 1 && pZ >= wz && pZ < wz + 1)
                        return true;
                }
            }
        }
        return false;
    }

    public void Jump()
    {
        if (IsOnGround)
        {
            VelocityY = GameConfig.JUMP_FORCE;
            IsOnGround = false;
        }
    }

    public void SwimStroke(Vector3 front)
    {
        if (_swimStrokeCooldown <= 0)
        {
            VelocityY += GameConfig.SWIM_STROKE_FORCE * 0.7f;
            _camera.Position.X += front.X * GameConfig.SWIM_STROKE_FORCE * 0.5f;
            _camera.Position.Z += front.Z * GameConfig.SWIM_STROKE_FORCE * 0.5f;
            _swimStrokeCooldown = 0.3f;
        }
    }

    public void StartStepUp(float targetY)
    {
        IsSteppingUp = true;
        _stepUpTarget = targetY;
        _stepUpProgress = 0;
    }
}
