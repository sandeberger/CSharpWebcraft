using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using CSharpWebcraft.Core;

namespace CSharpWebcraft.Rendering;

public class Camera
{
    public Vector3 Position;
    public Vector3 PlayerPosition;
    public float Yaw = -MathHelper.PiOver2; // Look along -Z initially
    public float Pitch;

    public float Fov = MathHelper.DegreesToRadians(75f);
    public float AspectRatio;
    public float NearPlane = 0.1f;
    public float FarPlane = 1000f;

    public Vector3 Front { get; private set; } = -Vector3.UnitZ;
    public Vector3 Up { get; private set; } = Vector3.UnitY;
    public Vector3 Right { get; private set; } = Vector3.UnitX;

    public Camera(Vector3 position, float aspectRatio)
    {
        Position = position;
        PlayerPosition = position;
        AspectRatio = aspectRatio;
        UpdateVectors();
    }

    public Matrix4 GetViewMatrix()
    {
        return Matrix4.LookAt(Position, Position + Front, Up);
    }

    public Matrix4 GetProjectionMatrix()
    {
        return Matrix4.CreatePerspectiveFieldOfView(Fov, AspectRatio, NearPlane, FarPlane);
    }

    public void ProcessMouseMovement(float deltaX, float deltaY)
    {
        Yaw += deltaX * GameConfig.MOUSE_SENSITIVITY;
        Pitch -= deltaY * GameConfig.MOUSE_SENSITIVITY;
        Pitch = System.Math.Clamp(Pitch, -MathHelper.PiOver2 + 0.01f, MathHelper.PiOver2 - 0.01f);
        UpdateVectors();
    }

    private void UpdateVectors()
    {
        Front = new Vector3(
            MathF.Cos(Pitch) * MathF.Cos(Yaw),
            MathF.Sin(Pitch),
            MathF.Cos(Pitch) * MathF.Sin(Yaw)
        );
        Front = Vector3.Normalize(Front);
        Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
        Up = Vector3.Normalize(Vector3.Cross(Right, Front));
    }
}
