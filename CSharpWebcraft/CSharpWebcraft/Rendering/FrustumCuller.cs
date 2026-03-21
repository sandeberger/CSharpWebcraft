using OpenTK.Mathematics;
using CSharpWebcraft.Core;

namespace CSharpWebcraft.Rendering;

public class FrustumCuller
{
    private Vector4[] _planes = new Vector4[6];
    private Vector3 _lastPos;
    private float _lastYaw;
    private float _lastPitch;
    private const float PosThreshold = 0.5f;
    private const float RotThreshold = 0.02f;

    public bool NeedsUpdate(Vector3 camPos, float yaw, float pitch)
    {
        return MathF.Abs(camPos.X - _lastPos.X) > PosThreshold ||
               MathF.Abs(camPos.Y - _lastPos.Y) > PosThreshold ||
               MathF.Abs(camPos.Z - _lastPos.Z) > PosThreshold ||
               MathF.Abs(yaw - _lastYaw) > RotThreshold ||
               MathF.Abs(pitch - _lastPitch) > RotThreshold;
    }

    public void Update(Matrix4 viewProjection, Vector3 camPos, float yaw, float pitch)
    {
        _lastPos = camPos;
        _lastYaw = yaw;
        _lastPitch = pitch;
        ExtractPlanes(viewProjection);
    }

    private void ExtractPlanes(Matrix4 m)
    {
        // Left
        _planes[0] = new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41);
        // Right
        _planes[1] = new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41);
        // Bottom
        _planes[2] = new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42);
        // Top
        _planes[3] = new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42);
        // Near
        _planes[4] = new Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43);
        // Far
        _planes[5] = new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43);

        // Normalize planes
        for (int i = 0; i < 6; i++)
        {
            float len = MathF.Sqrt(_planes[i].X * _planes[i].X + _planes[i].Y * _planes[i].Y + _planes[i].Z * _planes[i].Z);
            if (len > 0) _planes[i] /= len;
        }
    }

    public bool IsChunkVisible(int chunkX, int chunkZ)
    {
        float minX = chunkX * GameConfig.CHUNK_SIZE;
        float minY = 0;
        float minZ = chunkZ * GameConfig.CHUNK_SIZE;
        float maxX = minX + GameConfig.CHUNK_SIZE;
        float maxY = GameConfig.WORLD_HEIGHT;
        float maxZ = minZ + GameConfig.CHUNK_SIZE;

        for (int i = 0; i < 6; i++)
        {
            var p = _planes[i];
            float px = p.X > 0 ? maxX : minX;
            float py = p.Y > 0 ? maxY : minY;
            float pz = p.Z > 0 ? maxZ : minZ;

            if (p.X * px + p.Y * py + p.Z * pz + p.W < 0)
                return false;
        }
        return true;
    }
}
