using OpenTK.Mathematics;
using CSharpWebcraft.World;

namespace CSharpWebcraft.Mob;

/// <summary>
/// A* pathfinding for voxel terrain.
/// Ported from webcraft/js/pathfinding.js.
/// </summary>
public static class Pathfinding
{
    public struct PathfindOptions
    {
        public int MobHeight;
        public int MaxStepUp;
        public int MaxStepDown;
        public int MaxIterations;
        public int MaxPathLength;

        public static PathfindOptions Default => new()
        {
            MobHeight = 2,
            MaxStepUp = 1,
            MaxStepDown = 3,
            MaxIterations = 500,
            MaxPathLength = 50
        };
    }

    private struct PathNode
    {
        public int X, Y, Z;
        public float G, H, F;
        public int ParentIndex; // -1 = no parent

        public long Key => ((long)(X + 32768) << 32) | ((long)(Y & 0xFFFF) << 16) | (long)(Z + 32768 & 0xFFFF);
    }

    // 8 horizontal directions
    private static readonly (int dx, int dz)[] Directions =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (-1, 1), (1, -1), (-1, -1)
    };

    /// <summary>
    /// Find path from start to goal using A*.
    /// Returns list of world-space waypoints centered at block midpoints, or null.
    /// </summary>
    public static List<Vector3>? FindPath(Vector3 startPos, Vector3 goalPos,
        WorldManager world, PathfindOptions options)
    {
        int startX = (int)MathF.Floor(startPos.X);
        int startY = (int)MathF.Floor(startPos.Y);
        int startZ = (int)MathF.Floor(startPos.Z);
        int goalX = (int)MathF.Floor(goalPos.X);
        int goalY = (int)MathF.Floor(goalPos.Y);
        int goalZ = (int)MathF.Floor(goalPos.Z);

        // If goal too far, compute intermediate goal
        int directDist = Math.Abs(goalX - startX) + Math.Abs(goalZ - startZ);
        if (directDist > options.MaxPathLength * 2)
        {
            float ratio = (float)options.MaxPathLength / directDist;
            int limitedGoalX = (int)MathF.Floor(startX + (goalX - startX) * ratio);
            int limitedGoalZ = (int)MathF.Floor(startZ + (goalZ - startZ) * ratio);
            int limitedGoalY = goalY;
            for (int y = goalY + 5; y >= goalY - 10; y--)
            {
                if (IsWalkable(limitedGoalX, y, limitedGoalZ, world, options.MobHeight))
                {
                    limitedGoalY = y;
                    break;
                }
            }
            goalPos = new Vector3(limitedGoalX + 0.5f, limitedGoalY, limitedGoalZ + 0.5f);
            return FindPath(startPos, goalPos, world, options);
        }

        // Validate start position
        if (!IsWalkable(startX, startY, startZ, world, options.MobHeight))
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (IsWalkable(startX, startY + dy, startZ, world, options.MobHeight))
                {
                    startY += dy;
                    break;
                }
            }
        }

        // Node storage
        var nodes = new PathNode[options.MaxIterations + 64];
        int nodeCount = 0;

        // Open set (min-heap by F)
        var heap = new List<int>(128);
        var openMap = new Dictionary<long, int>(256);
        var closedSet = new HashSet<long>(256);

        // Start node
        var startNode = new PathNode
        {
            X = startX, Y = startY, Z = startZ,
            G = 0, ParentIndex = -1
        };
        startNode.H = Heuristic(startX, startY, startZ, goalX, goalY, goalZ);
        startNode.F = startNode.H;
        nodes[nodeCount] = startNode;
        HeapPush(heap, nodes, nodeCount);
        openMap[startNode.Key] = nodeCount;
        nodeCount++;

        int iterations = 0;

        while (heap.Count > 0 && iterations < options.MaxIterations)
        {
            iterations++;

            int currentIdx = HeapPop(heap, nodes);
            ref var current = ref nodes[currentIdx];
            long currentKey = current.Key;
            openMap.Remove(currentKey);

            // Goal check
            int distToGoal = Math.Abs(current.X - goalX) + Math.Abs(current.Z - goalZ);
            if (distToGoal <= 1 && Math.Abs(current.Y - goalY) <= 2)
                return ReconstructPath(nodes, currentIdx);

            closedSet.Add(currentKey);

            // Expand neighbors
            foreach (var (dx, dz) in Directions)
            {
                int newX = current.X + dx;
                int newZ = current.Z + dz;

                // Find ground level at new position
                for (int dy = options.MaxStepUp; dy >= -options.MaxStepDown; dy--)
                {
                    int newY = current.Y + dy;
                    if (newY < 0 || newY >= 100) continue;

                    if (!IsWalkable(newX, newY, newZ, world, options.MobHeight))
                        continue;

                    // Diagonal corner check
                    bool isDiagonal = dx != 0 && dz != 0;
                    if (isDiagonal)
                    {
                        bool canPassX = IsWalkable(current.X + dx, current.Y, current.Z, world, options.MobHeight) ||
                                        IsWalkable(current.X + dx, newY, current.Z, world, options.MobHeight);
                        bool canPassZ = IsWalkable(current.X, current.Y, current.Z + dz, world, options.MobHeight) ||
                                        IsWalkable(current.X, newY, current.Z + dz, world, options.MobHeight);
                        if (!canPassX && !canPassZ) continue;
                    }

                    long neighborKey = ((long)(newX + 32768) << 32) | ((long)(newY & 0xFFFF) << 16) | (long)(newZ + 32768 & 0xFFFF);

                    if (closedSet.Contains(neighborKey)) break;

                    float moveCost = isDiagonal ? 1.414f : 1f;
                    float verticalCost = Math.Abs(newY - current.Y) * 0.5f;
                    float tentativeG = current.G + moveCost + verticalCost;

                    if (openMap.TryGetValue(neighborKey, out int existingIdx))
                    {
                        if (tentativeG < nodes[existingIdx].G)
                        {
                            nodes[existingIdx].G = tentativeG;
                            nodes[existingIdx].F = tentativeG + nodes[existingIdx].H;
                            nodes[existingIdx].ParentIndex = currentIdx;
                        }
                    }
                    else if (nodeCount < nodes.Length)
                    {
                        var neighbor = new PathNode
                        {
                            X = newX, Y = newY, Z = newZ,
                            G = tentativeG,
                            H = Heuristic(newX, newY, newZ, goalX, goalY, goalZ),
                            ParentIndex = currentIdx
                        };
                        neighbor.F = neighbor.G + neighbor.H;
                        nodes[nodeCount] = neighbor;
                        HeapPush(heap, nodes, nodeCount);
                        openMap[neighborKey] = nodeCount;
                        nodeCount++;
                    }

                    break; // Found ground at this direction
                }
            }
        }

        return null; // No path found
    }

    /// <summary>
    /// Smooth path by removing waypoints with clear line of sight.
    /// </summary>
    public static List<Vector3> SmoothPath(List<Vector3> path, WorldManager world, int mobHeight = 2)
    {
        if (path == null || path.Count <= 2) return path ?? new List<Vector3>();

        var smoothed = new List<Vector3> { path[0] };
        int current = 0;

        while (current < path.Count - 1)
        {
            int furthest = current + 1;
            for (int i = current + 2; i < path.Count; i++)
            {
                if (HasLineOfSight(path[current], path[i], world, mobHeight))
                    furthest = i;
            }
            smoothed.Add(path[furthest]);
            current = furthest;
        }

        return smoothed;
    }

    /// <summary>
    /// Get next waypoint index, advancing past waypoints we've already reached.
    /// </summary>
    public static int GetNextWaypoint(List<Vector3> path, Vector3 currentPos,
        int waypointIndex, float arrivalThreshold = 0.5f)
    {
        while (waypointIndex < path.Count)
        {
            var wp = path[waypointIndex];
            float dx = wp.X - currentPos.X;
            float dz = wp.Z - currentPos.Z;
            if (dx * dx + dz * dz >= arrivalThreshold * arrivalThreshold)
                return waypointIndex;
            waypointIndex++;
        }
        return waypointIndex;
    }

    // ========== Private Helpers ==========

    private static bool IsWalkable(int x, int y, int z, WorldManager world, int mobHeight)
    {
        // Must have solid ground below
        byte groundBlock = world.GetBlockAt(x, y - 1, z);
        if (BlockRegistry.IsPassable(groundBlock))
            return false;

        // Must have space for mob height
        for (int h = 0; h < mobHeight; h++)
        {
            byte blockAtHeight = world.GetBlockAt(x, y + h, z);
            if (!BlockRegistry.IsPassable(blockAtHeight))
                return false;
        }

        return true;
    }

    private static float Heuristic(int ax, int ay, int az, int bx, int by, int bz)
    {
        return Math.Abs(ax - bx) + Math.Abs(az - bz) + Math.Abs(ay - by) * 2;
    }

    private static bool HasLineOfSight(Vector3 from, Vector3 to, WorldManager world, int mobHeight)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float dz = to.Z - from.Z;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = (int)MathF.Ceiling(distance * 2);
        if (steps <= 0) return true;

        for (int i = 1; i < steps; i++)
        {
            float t = (float)i / steps;
            float x = from.X + dx * t;
            float y = from.Y + dy * t;
            float z = from.Z + dz * t;

            if (!IsWalkable((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z), world, mobHeight))
                return false;
        }

        return true;
    }

    private static List<Vector3> ReconstructPath(PathNode[] nodes, int endIndex)
    {
        var path = new List<Vector3>();
        int current = endIndex;

        while (current >= 0)
        {
            ref var node = ref nodes[current];
            path.Add(new Vector3(node.X + 0.5f, node.Y, node.Z + 0.5f));
            current = node.ParentIndex;
        }

        path.Reverse();
        return path;
    }

    // ========== Binary Min-Heap ==========

    private static void HeapPush(List<int> heap, PathNode[] nodes, int nodeIndex)
    {
        heap.Add(nodeIndex);
        int index = heap.Count - 1;

        while (index > 0)
        {
            int parentIndex = (index - 1) / 2;
            if (nodes[heap[parentIndex]].F <= nodes[heap[index]].F) break;
            (heap[parentIndex], heap[index]) = (heap[index], heap[parentIndex]);
            index = parentIndex;
        }
    }

    private static int HeapPop(List<int> heap, PathNode[] nodes)
    {
        int min = heap[0];

        if (heap.Count == 1)
        {
            heap.Clear();
            return min;
        }

        heap[0] = heap[^1];
        heap.RemoveAt(heap.Count - 1);

        int index = 0;
        int length = heap.Count;
        while (true)
        {
            int left = 2 * index + 1;
            int right = 2 * index + 2;
            int smallest = index;

            if (left < length && nodes[heap[left]].F < nodes[heap[smallest]].F)
                smallest = left;
            if (right < length && nodes[heap[right]].F < nodes[heap[smallest]].F)
                smallest = right;
            if (smallest == index) break;

            (heap[smallest], heap[index]) = (heap[index], heap[smallest]);
            index = smallest;
        }

        return min;
    }
}
