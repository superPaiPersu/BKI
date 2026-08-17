using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityStateSim.Pathfinding
{
    public sealed class WorldGridPathfinder : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField, Min(0.05f)] private float cellSize = 0.5f;
        [SerializeField, Min(1)] private int maxSearchNodes = 4000;
        [SerializeField, Min(1)] private int minimumSearchNodes = 12000;
        [SerializeField, Min(1)] private int nearestWalkableSearchRadius = 32;

        [Header("Walkable Area")]
        [SerializeField] private bool requireWalkableArea;
        [SerializeField] private LayerMask walkableLayers;

        [Header("Collision")]
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField, Min(0.01f)] private float agentRadius = 0.2f;
        [SerializeField] private bool ignoreDecorativeTilemapColliders = true;
        [SerializeField] private string[] ignoredDecorativeTilemapNameTokens =
            { "display", "decoration", "decor", "visual", "render", "walkable" };

        [Header("Debug")]
        [SerializeField] private bool logFailures;
        [SerializeField] private bool drawLastPath;

        private const float NarrowSpacePenaltyWeight = 0.08f;

        private readonly Collider2D[] overlapResults = new Collider2D[16];
        private readonly RaycastHit2D[] castResults = new RaycastHit2D[16];
        private readonly List<Vector2> lastPath = new List<Vector2>();

        private static readonly Vector2Int[] CardinalDirections =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        public float CellSize => cellSize;
        public float AgentRadius => agentRadius;

        public bool TryFindPath(Vector2 start, Vector2 goal, Collider2D ignoredCollider, List<Vector2> path)
        {
            return TryFindPath(start, goal, ignoredCollider, agentRadius, path);
        }

        public bool TryFindPath(Vector2 start, Vector2 goal, Collider2D ignoredCollider, float clearanceRadius, List<Vector2> path)
        {
            path.Clear();
            lastPath.Clear();
            float radius = NormalizeRadius(clearanceRadius);

            Vector2Int startCell = WorldToCell(start);
            Vector2Int goalCell = WorldToCell(goal);
            Vector2 finalGoal = goal;

            if (!IsWalkable(startCell, ignoredCollider, radius))
            {
                if (!TryFindNearestWalkableCell(startCell, ignoredCollider, radius, out Vector2Int nearestStartCell))
                {
                    LogFailure($"No walkable start cell near {start}. clearanceRadius={radius:0.###}.");
                    return false;
                }

                startCell = nearestStartCell;
            }

            if (!IsWalkable(goalCell, ignoredCollider, radius))
            {
                if (!TryFindNearestWalkableCell(goalCell, ignoredCollider, radius, out Vector2Int nearestGoalCell))
                {
                    LogFailure($"No walkable goal cell near {goal}. clearanceRadius={radius:0.###}.");
                    return false;
                }

                goalCell = nearestGoalCell;
                finalGoal = CellToWorld(goalCell);
            }

            if (startCell == goalCell)
            {
                AddAxisAlignedSegment(path, start, finalGoal);
                lastPath.AddRange(path);
                return true;
            }

            Dictionary<Vector2Int, PathNode> nodes = new Dictionary<Vector2Int, PathNode>();
            List<PathNode> open = new List<PathNode>();
            HashSet<Vector2Int> closed = new HashSet<Vector2Int>();

            PathNode startNode = GetOrCreateNode(nodes, startCell);
            startNode.gCost = 0f;
            startNode.hCost = Heuristic(startCell, goalCell);
            open.Add(startNode);
            Dictionary<Vector2Int, float> traversalPenaltyCache = new Dictionary<Vector2Int, float>();

            int searchLimit = GetSearchLimit(startCell, goalCell);
            int searchedNodes = 0;
            while (open.Count > 0 && searchedNodes < searchLimit)
            {
                searchedNodes++;
                PathNode current = PopLowestCost(open);

                if (current.cell == goalCell)
                {
                    BuildPath(current, start, finalGoal, path);
                    SmoothPath(path, ignoredCollider, radius);
                    lastPath.AddRange(path);
                    return true;
                }

                closed.Add(current.cell);
                AddNeighbors(current, goalCell, ignoredCollider, radius, nodes, closed, open, traversalPenaltyCache);
            }

            LogFailure($"No path from {start} to {goal}. Searched {searchedNodes}/{searchLimit} nodes. clearanceRadius={radius:0.###}.");

            return false;
        }

        private void AddNeighbors(
            PathNode current,
            Vector2Int goalCell,
            Collider2D ignoredCollider,
            float clearanceRadius,
            Dictionary<Vector2Int, PathNode> nodes,
            HashSet<Vector2Int> closed,
            List<PathNode> open,
            Dictionary<Vector2Int, float> traversalPenaltyCache)
        {
            AddNeighborSet(CardinalDirections, current, goalCell, ignoredCollider, clearanceRadius, nodes, closed, open, traversalPenaltyCache, 1f);
        }

        private void AddNeighborSet(
            Vector2Int[] directions,
            PathNode current,
            Vector2Int goalCell,
            Collider2D ignoredCollider,
            float clearanceRadius,
            Dictionary<Vector2Int, PathNode> nodes,
            HashSet<Vector2Int> closed,
            List<PathNode> open,
            Dictionary<Vector2Int, float> traversalPenaltyCache,
            float moveCost)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int neighborCell = current.cell + directions[i];
                if (closed.Contains(neighborCell) || !IsWalkable(neighborCell, ignoredCollider, clearanceRadius))
                {
                    continue;
                }

                PathNode neighbor = GetOrCreateNode(nodes, neighborCell);
                float newCost = current.gCost + moveCost + GetTraversalPenalty(neighborCell, ignoredCollider, clearanceRadius, traversalPenaltyCache);
                if (open.Contains(neighbor) && newCost >= neighbor.gCost)
                {
                    continue;
                }

                neighbor.parent = current;
                neighbor.gCost = newCost;
                neighbor.hCost = Heuristic(neighborCell, goalCell);

                if (!open.Contains(neighbor))
                {
                    open.Add(neighbor);
                }
            }
        }

        private float GetTraversalPenalty(
            Vector2Int cell,
            Collider2D ignoredCollider,
            float clearanceRadius,
            Dictionary<Vector2Int, float> traversalPenaltyCache)
        {
            if (traversalPenaltyCache.TryGetValue(cell, out float cachedPenalty))
            {
                return cachedPenalty;
            }

            int openNeighborCount = 0;
            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                if (IsWalkable(cell + CardinalDirections[i], ignoredCollider, clearanceRadius))
                {
                    openNeighborCount++;
                }
            }

            float penalty = (CardinalDirections.Length - openNeighborCount) * NarrowSpacePenaltyWeight;
            if (openNeighborCount <= 1)
            {
                penalty += NarrowSpacePenaltyWeight * 0.5f;
            }

            traversalPenaltyCache[cell] = penalty;
            return penalty;
        }

        private bool IsWalkable(Vector2Int cell, Collider2D ignoredCollider)
        {
            return IsWalkable(cell, ignoredCollider, agentRadius);
        }

        private bool IsWalkable(Vector2Int cell, Collider2D ignoredCollider, float clearanceRadius)
        {
            Vector2 center = CellToWorld(cell);
            if (requireWalkableArea && !HasWalkableArea(center, ignoredCollider))
            {
                return false;
            }

            int count = Physics2D.OverlapCircleNonAlloc(center, NormalizeRadius(clearanceRadius), overlapResults, obstacleLayers);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapResults[i];
                if (ShouldIgnoreCollider(hit, ignoredCollider))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        public bool HasClearSegment(Vector2 from, Vector2 to, Collider2D ignoredCollider)
        {
            return HasClearSegment(from, to, ignoredCollider, agentRadius);
        }

        public bool HasClearSegment(Vector2 from, Vector2 to, Collider2D ignoredCollider, float clearanceRadius)
        {
            if (!IsAxisAlignedSegment(from, to))
            {
                return false;
            }

            Vector2 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
            {
                return true;
            }

            Vector2 direction = delta / distance;
            int count = Physics2D.CircleCastNonAlloc(from, NormalizeRadius(clearanceRadius), direction, castResults, distance, obstacleLayers);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = castResults[i].collider;
                if (ShouldIgnoreCollider(hit, ignoredCollider))
                {
                    continue;
                }

                return false;
            }

            if (!requireWalkableArea)
            {
                return true;
            }

            float step = Mathf.Max(cellSize * 0.5f, 0.05f);
            int sampleCount = Mathf.CeilToInt(distance / step);
            for (int i = 0; i <= sampleCount; i++)
            {
                float t = sampleCount == 0 ? 1f : i / (float)sampleCount;
                if (!HasWalkableArea(Vector2.Lerp(from, to, t), ignoredCollider))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasWalkableArea(Vector2 center, Collider2D ignoredCollider)
        {
            int count = Physics2D.OverlapCircleNonAlloc(center, cellSize * 0.25f, overlapResults, walkableLayers);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapResults[i];
                if (hit == null || hit == ignoredCollider)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private Vector2Int FindNearestWalkableCell(Vector2Int origin, Collider2D ignoredCollider)
        {
            return FindNearestWalkableCell(origin, ignoredCollider, agentRadius);
        }

        private Vector2Int FindNearestWalkableCell(Vector2Int origin, Collider2D ignoredCollider, float clearanceRadius)
        {
            return TryFindNearestWalkableCell(origin, ignoredCollider, clearanceRadius, out Vector2Int cell)
                ? cell
                : origin;
        }

        private bool TryFindNearestWalkableCell(Vector2Int origin, Collider2D ignoredCollider, float clearanceRadius, out Vector2Int bestCell)
        {
            int maxRadius = Mathf.Max(1, nearestWalkableSearchRadius);
            bestCell = origin;
            float bestScore = float.PositiveInfinity;
            bool found = false;

            for (int radius = 1; radius <= maxRadius; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                        {
                            continue;
                        }

                        Vector2Int candidate = origin + new Vector2Int(x, y);
                        if (IsWalkable(candidate, ignoredCollider, clearanceRadius))
                        {
                            float score = ScoreWalkableCandidate(origin, candidate, ignoredCollider, clearanceRadius);
                            if (!found || score < bestScore)
                            {
                                bestCell = candidate;
                                bestScore = score;
                                found = true;
                            }
                        }
                    }
                }
            }

            return found;
        }

        private float ScoreWalkableCandidate(Vector2Int origin, Vector2Int candidate, Collider2D ignoredCollider, float clearanceRadius)
        {
            float distanceScore = (candidate - origin).sqrMagnitude;
            int openNeighborCount = 0;
            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                if (IsWalkable(candidate + CardinalDirections[i], ignoredCollider, clearanceRadius))
                {
                    openNeighborCount++;
                }
            }

            return distanceScore - openNeighborCount * 0.05f;
        }

        private void SmoothPath(List<Vector2> path, Collider2D ignoredCollider)
        {
            SmoothPath(path, ignoredCollider, agentRadius);
        }

        private void SmoothPath(List<Vector2> path, Collider2D ignoredCollider, float clearanceRadius)
        {
            if (path.Count <= 2)
            {
                return;
            }

            List<Vector2> smoothed = new List<Vector2>();
            int currentIndex = 0;
            smoothed.Add(path[currentIndex]);

            while (currentIndex < path.Count - 1)
            {
                int nextIndex = currentIndex + 1;
                for (int candidate = path.Count - 1; candidate > nextIndex; candidate--)
                {
                    if (HasClearSegment(path[currentIndex], path[candidate], ignoredCollider, clearanceRadius))
                    {
                        nextIndex = candidate;
                        break;
                    }
                }

                smoothed.Add(path[nextIndex]);
                currentIndex = nextIndex;
            }

            path.Clear();
            path.AddRange(smoothed);
        }

        private float NormalizeRadius(float radius)
        {
            return Mathf.Max(0.01f, radius);
        }

        private int GetSearchLimit(Vector2Int startCell, Vector2Int goalCell)
        {
            int dx = Mathf.Abs(startCell.x - goalCell.x);
            int dy = Mathf.Abs(startCell.y - goalCell.y);
            int distanceBasedLimit = Mathf.CeilToInt((dx + dy + 1) * 48f);
            return Mathf.Max(maxSearchNodes, minimumSearchNodes, distanceBasedLimit);
        }

        private bool ShouldIgnoreCollider(Collider2D hit, Collider2D ignoredCollider)
        {
            return hit == null
                || hit == ignoredCollider
                || hit.isTrigger
                || NavigationColliderFilter.ShouldIgnoreDecorativeTilemap(
                    hit,
                    ignoreDecorativeTilemapColliders,
                    ignoredDecorativeTilemapNameTokens);
        }

        private void LogFailure(string message)
        {
            if (logFailures)
            {
                Debug.LogWarning($"[Pathfinding] {message}", this);
            }
        }

        private void BuildPath(PathNode endNode, Vector2 exactStart, Vector2 exactGoal, List<Vector2> path)
        {
            List<Vector2> reversed = new List<Vector2>();
            PathNode current = endNode;
            while (current != null)
            {
                reversed.Add(CellToWorld(current.cell));
                current = current.parent;
            }

            for (int i = reversed.Count - 1; i >= 0; i--)
            {
                path.Add(reversed[i]);
            }

            if (path.Count == 0)
            {
                AddAxisAlignedSegment(path, exactStart, exactGoal);
                return;
            }

            List<Vector2> axisAlignedPath = new List<Vector2>();
            axisAlignedPath.Add(exactStart);
            int firstCellIndex = path.Count > 1 ? 1 : 0;
            for (int i = firstCellIndex; i < path.Count; i++)
            {
                AddAxisAlignedSegment(axisAlignedPath, path[i]);
            }

            AddAxisAlignedSegment(axisAlignedPath, exactGoal);
            path.Clear();
            path.AddRange(axisAlignedPath);
        }

        private void AddAxisAlignedSegment(List<Vector2> path, Vector2 point)
        {
            if (path.Count == 0)
            {
                path.Add(point);
                return;
            }

            AddAxisAlignedSegment(path, path[path.Count - 1], point);
        }

        private void AddAxisAlignedSegment(List<Vector2> path, Vector2 from, Vector2 to)
        {
            if (path.Count == 0)
            {
                path.Add(from);
            }

            if ((to - from).sqrMagnitude <= 0.000001f)
            {
                return;
            }

            if (IsAxisAlignedSegment(from, to))
            {
                AddDistinctPoint(path, to);
                return;
            }

            Vector2 corner = new Vector2(to.x, from.y);
            AddDistinctPoint(path, corner);
            AddDistinctPoint(path, to);
        }

        private static void AddDistinctPoint(List<Vector2> path, Vector2 point)
        {
            if (path.Count > 0 && (path[path.Count - 1] - point).sqrMagnitude <= 0.000001f)
            {
                return;
            }

            path.Add(point);
        }

        private static bool IsAxisAlignedSegment(Vector2 from, Vector2 to)
        {
            const float Tolerance = 0.0001f;
            return Mathf.Abs(from.x - to.x) <= Tolerance || Mathf.Abs(from.y - to.y) <= Tolerance;
        }

        private Vector2Int WorldToCell(Vector2 world)
        {
            return new Vector2Int(
                Mathf.RoundToInt(world.x / cellSize),
                Mathf.RoundToInt(world.y / cellSize));
        }

        private Vector2 CellToWorld(Vector2Int cell)
        {
            return new Vector2(cell.x * cellSize, cell.y * cellSize);
        }

        private static PathNode GetOrCreateNode(Dictionary<Vector2Int, PathNode> nodes, Vector2Int cell)
        {
            if (!nodes.TryGetValue(cell, out PathNode node))
            {
                node = new PathNode(cell);
                nodes.Add(cell, node);
            }

            return node;
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return dx + dy;
        }

        private static PathNode PopLowestCost(List<PathNode> open)
        {
            int bestIndex = 0;
            PathNode best = open[0];
            for (int i = 1; i < open.Count; i++)
            {
                PathNode node = open[i];
                if (node.FCost < best.FCost || Mathf.Approximately(node.FCost, best.FCost) && node.hCost < best.hCost)
                {
                    bestIndex = i;
                    best = node;
                }
            }

            open.RemoveAt(bestIndex);
            return best;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawLastPath || lastPath.Count == 0)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < lastPath.Count; i++)
            {
                Gizmos.DrawWireSphere(lastPath[i], agentRadius);
                if (i > 0)
                {
                    Gizmos.DrawLine(lastPath[i - 1], lastPath[i]);
                }
            }
        }

        private sealed class PathNode
        {
            public readonly Vector2Int cell;
            public PathNode parent;
            public float gCost = float.PositiveInfinity;
            public float hCost;

            public float FCost => gCost + hCost;

            public PathNode(Vector2Int cell)
            {
                this.cell = cell;
            }
        }
    }

    public static class NavigationColliderFilter
    {
        private static readonly string[] DefaultDecorativeTilemapNameTokens =
        {
            "display",
            "decoration",
            "decor",
            "visual",
            "render",
            "walkable"
        };

        private static readonly string[] BlockingNameTokens =
        {
            "collision",
            "obstacle",
            "block",
            "wall"
        };

        public static bool ShouldIgnoreDecorativeTilemap(Collider2D collider, bool enabled, string[] ignoredNameTokens)
        {
            if (!enabled || collider == null || collider is not TilemapCollider2D)
            {
                return false;
            }

            string hierarchyName = BuildHierarchyName(collider.transform);
            if (ContainsAny(hierarchyName, BlockingNameTokens))
            {
                return false;
            }

            string[] tokens = ignoredNameTokens != null && ignoredNameTokens.Length > 0
                ? ignoredNameTokens
                : DefaultDecorativeTilemapNameTokens;
            return ContainsAny(hierarchyName, tokens);
        }

        private static string BuildHierarchyName(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            string text = transform.name;
            Transform parent = transform.parent;
            int depth = 0;
            while (parent != null && depth < 4)
            {
                text += " " + parent.name;
                parent = parent.parent;
                depth++;
            }

            return text;
        }

        private static bool ContainsAny(string text, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(text) || tokens == null)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (ContainsLoose(text, tokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLoose(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return NormalizeLoose(text).IndexOf(NormalizeLoose(token), StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeLoose(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.ToLowerInvariant()
                    .Replace("'", string.Empty)
                    .Replace(" ", string.Empty)
                    .Replace("_", string.Empty)
                    .Replace("-", string.Empty);
        }
    }
}
