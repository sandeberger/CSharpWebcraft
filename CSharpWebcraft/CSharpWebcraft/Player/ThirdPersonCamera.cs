using OpenTK.Mathematics;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Player;

public class ThirdPersonCamera
{
    private const float DefaultDistance = 4.0f;
    private const float HeightOffset = 0.3f;
    private const float MinDistance = 0.5f;
    private const float RayStep = 0.2f;

    public float Distance { get; set; } = DefaultDistance;

    public Vector3 CalculateCameraPosition(Vector3 playerEyePos, float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        float sinPitch = MathF.Sin(pitch);
        float cosYaw = MathF.Cos(yaw);
        float sinYaw = MathF.Sin(yaw);

        // Camera sits behind the player (opposite of look direction)
        Vector3 backward = new Vector3(
            -(cosPitch * cosYaw),
            -(sinPitch),
            -(cosPitch * sinYaw)
        );

        return playerEyePos + backward * Distance + new Vector3(0, HeightOffset, 0);
    }

    public Vector3 ClampToWalls(Vector3 playerEyePos, Vector3 desiredCameraPos, WorldManager world)
    {
        Vector3 direction = desiredCameraPos - playerEyePos;
        float maxDist = direction.Length;
        if (maxDist < 0.01f) return desiredCameraPos;

        direction /= maxDist;

        float safeDist = maxDist;

        for (float d = RayStep; d < maxDist; d += RayStep)
        {
            Vector3 checkPos = playerEyePos + direction * d;
            int bx = (int)MathF.Floor(checkPos.X);
            int by = (int)MathF.Floor(checkPos.Y);
            int bz = (int)MathF.Floor(checkPos.Z);

            if (!BlockRegistry.IsPassable(world.GetBlockAt(bx, by, bz)))
            {
                safeDist = d - RayStep * 0.5f;
                break;
            }
        }

        safeDist = MathF.Max(safeDist, MinDistance);
        return playerEyePos + direction * safeDist;
    }
}
