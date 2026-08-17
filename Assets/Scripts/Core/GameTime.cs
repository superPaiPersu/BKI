using System;
using UnityEngine;

namespace CityStateSim.Core
{
    [Serializable]
    public struct GameTime : IComparable<GameTime>, IEquatable<GameTime>
    {
        [SerializeField] private int hour;
        [SerializeField] private int minute;

        public int Hour => hour;
        public int Minute => minute;
        public int TotalMinutes => hour * 60 + minute;

        public GameTime(int hour, int minute)
        {
            int totalMinutes = Mathf.Clamp(hour * 60 + minute, 0, 1439);
            this.hour = totalMinutes / 60;
            this.minute = totalMinutes % 60;
        }

        public static GameTime FromTotalMinutes(int totalMinutes)
        {
            totalMinutes = ((totalMinutes % 1440) + 1440) % 1440;
            return new GameTime(totalMinutes / 60, totalMinutes % 60);
        }

        public int CompareTo(GameTime other)
        {
            return TotalMinutes.CompareTo(other.TotalMinutes);
        }

        public bool Equals(GameTime other)
        {
            return hour == other.hour && minute == other.minute;
        }

        public override bool Equals(object obj)
        {
            return obj is GameTime other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(hour, minute);
        }

        public override string ToString()
        {
            return $"{hour:00}:{minute:00}";
        }
    }
}
