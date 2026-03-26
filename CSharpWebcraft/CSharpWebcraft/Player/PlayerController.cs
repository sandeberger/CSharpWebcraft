using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CSharpWebcraft.Core;
using CSharpWebcraft.Input;
using CSharpWebcraft.Rendering;
using CSharpWebcraft.UI;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Player;

public class PlayerController
{
    private readonly Camera _camera;
    private readonly InputManager _input;
    private readonly WorldManager _world;
    private readonly PlayerPhysics _physics;
    private readonly BlockInteraction _blockInteraction;
    private readonly HudRenderer _hud;

    public PlayerPhysics Physics => _physics;
    public BlockInteraction BlockInteraction => _blockInteraction;
    public float SpeedMultiplier { get; set; } = 1f;
    public bool FlyMode { get; set; }

    public PlayerController(Camera camera, InputManager input, WorldManager world,
        HudRenderer hud, WaterFlow? waterFlow = null, LavaFlow? lavaFlow = null)
    {
        _camera = camera;
        _input = input;
        _world = world;
        _hud = hud;
        _physics = new PlayerPhysics(camera, world);
        _blockInteraction = new BlockInteraction(camera, world, waterFlow, lavaFlow);
    }

    public void Update(float deltaTime)
    {
        // Mouse look
        _camera.ProcessMouseMovement(_input.MouseDeltaX, _input.MouseDeltaY);

        // Movement
        HandleMovement(deltaTime);

        // Physics
        _physics.Update(deltaTime);

        // Block interaction
        if (_input.IsMouseButtonPressed(MouseButton.Left))
            _blockInteraction.BreakBlock();
        if (_input.IsMouseButtonPressed(MouseButton.Right))
            _blockInteraction.PlaceBlock(_hud.SelectedBlockType);
    }

    private void HandleMovement(float deltaTime)
    {
        var moveDir = Vector3.Zero;
        Vector3 front = _camera.Front;
        Vector3 right = _camera.Right;

        if (_input.IsKeyDown(Keys.W)) moveDir += front;
        if (_input.IsKeyDown(Keys.S)) moveDir -= front;
        if (_input.IsKeyDown(Keys.A)) moveDir -= right;
        if (_input.IsKeyDown(Keys.D)) moveDir += right;

        bool isUnderwater = _physics.IsUnderwater;

        if (FlyMode)
        {
            // Fly mode: move in camera direction, space=up, shift=down
            if (moveDir.LengthSquared > 0.001f)
                moveDir = Vector3.Normalize(moveDir);
            if (_input.IsKeyDown(Keys.Space)) moveDir.Y += 1f;
            if (_input.IsKeyDown(Keys.LeftShift)) moveDir.Y -= 1f;
            if (moveDir.LengthSquared > 0.001f)
                moveDir = Vector3.Normalize(moveDir);
            moveDir *= GameConfig.MOVEMENT_SPEED * SpeedMultiplier * 2f;
            _camera.Position += moveDir;
            _physics.VelocityY = 0;
            return;
        }

        moveDir.Y = 0;
        if (moveDir.LengthSquared > 0.001f)
            moveDir = Vector3.Normalize(moveDir);

        float speed = isUnderwater ? GameConfig.WATER_MOVEMENT_SPEED : GameConfig.MOVEMENT_SPEED * (_physics.IsOnGround ? 1f : 0.8f);
        speed *= SpeedMultiplier;
        moveDir *= speed;

        // Step-based movement with collision
        int steps = 5;
        Vector3 stepMove = moveDir / steps;

        for (int i = 0; i < steps; i++)
        {
            if (stepMove.X != 0)
            {
                float newX = _camera.Position.X + stepMove.X;
                if (!_physics.CheckCollision(newX, _camera.Position.Y, _camera.Position.Z))
                    _camera.Position.X = newX;
                else if (_physics.IsOnGround && !isUnderwater)
                {
                    float sUY = _camera.Position.Y + GameConfig.STEP_UP_HEIGHT;
                    if (!_physics.CheckCollision(_camera.Position.X, sUY + GameConfig.PLAYER_HEIGHT * 0.8f, _camera.Position.Z) &&
                        !_physics.CheckCollision(newX, sUY, _camera.Position.Z))
                    {
                        _physics.StartStepUp(sUY);
                        _camera.Position.X = newX;
                    }
                }
            }

            if (stepMove.Z != 0)
            {
                float newZ = _camera.Position.Z + stepMove.Z;
                if (!_physics.CheckCollision(_camera.Position.X, _camera.Position.Y, newZ))
                    _camera.Position.Z = newZ;
                else if (_physics.IsOnGround && !isUnderwater)
                {
                    float sUY = _camera.Position.Y + GameConfig.STEP_UP_HEIGHT;
                    if (!_physics.CheckCollision(_camera.Position.X, sUY + GameConfig.PLAYER_HEIGHT * 0.8f, _camera.Position.Z) &&
                        !_physics.CheckCollision(_camera.Position.X, sUY, newZ))
                    {
                        _physics.StartStepUp(sUY);
                        _camera.Position.Z = newZ;
                    }
                }
            }
        }

        // Jump / swim
        if (_input.IsKeyDown(Keys.Space))
        {
            if (isUnderwater)
                _physics.SwimStroke(_camera.Front);
            else if (_physics.IsOnGround)
                _physics.Jump();
        }
    }
}
