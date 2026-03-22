using OpenTK.Mathematics;

namespace CSharpWebcraft.Mob;

/// <summary>
/// Boid-style flocking for CubeSlimes when in groups.
/// Applies separation, alignment, and cohesion forces.
/// </summary>
public static class FlockingSystem
{
    private const float NeighborRadius = 6f;
    private const float SeparationRadius = 1.5f;
    private const float SeparationWeight = 0.08f;
    private const float AlignmentWeight = 0.03f;
    private const float CohesionWeight = 0.02f;

    public static void Update(List<CubeSlime> slimes)
    {
        if (slimes.Count < 2) return;

        float neighborRadSq = NeighborRadius * NeighborRadius;
        float separationRadSq = SeparationRadius * SeparationRadius;

        for (int i = 0; i < slimes.Count; i++)
        {
            var slime = slimes[i];
            if (!slime.IsAlive) continue;

            Vector3 separation = Vector3.Zero;
            Vector3 avgVelocity = Vector3.Zero;
            Vector3 avgPosition = Vector3.Zero;
            int neighborCount = 0;
            int separationCount = 0;

            for (int j = 0; j < slimes.Count; j++)
            {
                if (i == j || !slimes[j].IsAlive) continue;

                Vector3 diff = slime.Position - slimes[j].Position;
                float distSq = diff.LengthSquared;

                if (distSq < neighborRadSq)
                {
                    avgVelocity.X += slimes[j].Velocity.X;
                    avgVelocity.Z += slimes[j].Velocity.Z;
                    avgPosition += slimes[j].Position;
                    neighborCount++;

                    // Separation: push away from very close neighbors
                    if (distSq < separationRadSq && distSq > 0.001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        separation += diff / dist * (1f - dist / SeparationRadius);
                        separationCount++;
                    }
                }
            }

            if (neighborCount == 0)
            {
                slime.FlockVelocity = Vector3.Zero;
                continue;
            }

            Vector3 flockForce = Vector3.Zero;

            // Separation
            if (separationCount > 0)
            {
                separation /= separationCount;
                flockForce += separation * SeparationWeight;
            }

            // Alignment: steer toward average heading
            avgVelocity /= neighborCount;
            flockForce.X += avgVelocity.X * AlignmentWeight;
            flockForce.Z += avgVelocity.Z * AlignmentWeight;

            // Cohesion: steer toward center of neighbors
            avgPosition /= neighborCount;
            Vector3 toCenter = avgPosition - slime.Position;
            flockForce.X += toCenter.X * CohesionWeight;
            flockForce.Z += toCenter.Z * CohesionWeight;

            slime.FlockVelocity = flockForce;
        }
    }
}
