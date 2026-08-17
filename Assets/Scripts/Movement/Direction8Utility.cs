using UnityEngine;

namespace CityStateSim.Movement
{
    public static class Direction8Utility
    {
        public static Direction8 FromVector(Vector2 vector, Direction8 fallback = Direction8.South)
        {
            if (vector.sqrMagnitude < 0.0001f)
            {
                return fallback;
            }

            float angle = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }

            int octant = Mathf.RoundToInt(angle / 45f) % 8;
            return octant switch
            {
                0 => Direction8.East,
                1 => Direction8.NorthEast,
                2 => Direction8.North,
                3 => Direction8.NorthWest,
                4 => Direction8.West,
                5 => Direction8.SouthWest,
                6 => Direction8.South,
                7 => Direction8.SouthEast,
                _ => fallback
            };
        }

        public static Vector2 ToVector(Direction8 direction)
        {
            return direction switch
            {
                Direction8.South => Vector2.down,
                Direction8.SouthEast => new Vector2(1f, -1f).normalized,
                Direction8.East => Vector2.right,
                Direction8.NorthEast => new Vector2(1f, 1f).normalized,
                Direction8.North => Vector2.up,
                Direction8.NorthWest => new Vector2(-1f, 1f).normalized,
                Direction8.West => Vector2.left,
                Direction8.SouthWest => new Vector2(-1f, -1f).normalized,
                _ => Vector2.down
            };
        }
    }
}
