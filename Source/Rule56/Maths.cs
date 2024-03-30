using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
namespace CombatAI
{
    public static class Maths
    {
        private const int DistTh1 = 35 * 35;
        private const int DistTh2 = 70 * 70;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceTo_Fast(this IntVec3 first, IntVec3 second)
        {
            float a       = first.x - second.x;
            float b       = first.z - second.z;
            float distSqr = a * a + b * b;
            if (distSqr < DistTh1)
            {
                return Sqrt_Fast(distSqr, 3);
            }
            if (distSqr < DistTh2)
            {
                return Sqrt_Fast(distSqr, 5);
            }
            return Sqrt_Fast(distSqr, 7);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceTo_Fast(this Thing first, Thing second)
        {
            return first.Position.DistanceTo_Fast(second.Position);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntVec3 Round(this Vector3 vector)
        {
	        return new IntVec3(Mathf.RoundToInt(vector.x), Mathf.RoundToInt(vector.y), Mathf.RoundToInt(vector.z));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Max(int a, int b)
        {
            return a > b ? a : b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Max(short a, short b)
        {
            return a > b ? a : b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Max(byte a, byte b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float a, float b, float c)
        {
            float temp;
            return (temp = a > b ? a : b) > c ? temp : c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Max(int a, int b, int c)
        {
            int temp;
            return (temp = a > b ? a : b) > c ? temp : c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Max(short a, short b, short c)
        {
            short temp;
            return (temp = a > b ? a : b) > c ? temp : c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Max(byte a, byte b, byte c)
        {
            byte temp;
            return (temp = a > b ? a : b) > c ? temp : c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int a, int b)
        {
            return a < b ? a : b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Min(short a, short b)
        {
            return a < b ? a : b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Min(byte a, byte b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float a, float b, float c)
        {
            float temp;
            return (temp = a < b ? a : b) < c ? temp : c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int a, int b, int c)
        {
            int temp;
            return (temp = a < b ? a : b) < c ? temp : c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Min(short a, short b, short c)
        {
            short temp;
            return (temp = a < b ? a : b) < c ? temp : c;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Min(byte a, byte b, byte c)
        {
            byte temp;
            return (temp = a < b ? a : b) < c ? temp : c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqr(float a)
        {
            return a * a;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sqr(int a)
        {
            return a * a;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Sqr(short a)
        {
            return (short)(a * a);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Sqr(byte a)
        {
            return (byte)(a * a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt_Fast(float x, int iterations)
        {
            if (x < 0.001f)
            {
                return iterations >= 5 ? Mathf.Sqrt(x) : x;
            }
            if (x < 0)
            {
                throw new Exception("Input cannot be a negative value");
            }
            int n;
            int k;
            int a = (int)(x * 1024f);
            if ((a & 0xFFFF0000) != 0)
            {
                if ((a & 0xFFF00000) != 0)
                {
                    n = 20;
                }
                else
                {
                    n = 16;
                }
            }
            else
            {
                if ((a & 0xFFFFF000) != 0)
                {
                    n = 12;
                }
                else if ((a & 0xFFFFFFC0) != 0)
                {
                    n = 6;
                }
                else
                {
                    n = 0;
                }
            }
            k = a >> n;
            while (k != 0)
            {
                k = k >> 1;
                n++;
            }
            int bot = 1 << (n - 1 >> 1);
            int top = bot << 1;
            while (iterations-- > 0)
            {
                int mid    = bot + top >> 1;
                int midSqr = mid * mid;
                if (midSqr == a)
                {
                    return mid / 32f;
                }
                if (midSqr < a)
                {
                    bot = mid;
                }
                else
                {
                    top = mid;
                }
            }
            return (bot + top) / 64f;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sqrt_Fast(int a, int iterations)
        {
            if (a == 0)
            {
                return 0;
            }
            if (a < 0)
            {
                throw new Exception("Input cannot be a negative value");
            }
            int n;
            int k;
            if ((a & 0xFFFFFF00) != 0)
            {
                if ((a & 0xFFFFF000) != 0)
                {
                    n = 12;
                }
                else
                {
                    n = 8;
                }
            }
            else
            {
                if ((a & 0xFFFFFFF0) != 0)
                {
                    n = 4;
                }
                else
                {
                    n = 0;
                }
            }
            k = a >> n;
            while (k != 0)
            {
                k = k >> 1;
                n++;
            }
            int bot = 1 << (n - 1 >> 1);
            int top = bot << 1;
            while (iterations-- > 0)
            {
                int mid    = bot + top >> 1;
                int midSqr = mid * mid;
                if (midSqr == a)
                {
                    return mid;
                }
                if (midSqr < a)
                {
                    bot = mid;
                }
                else
                {
                    top = mid;
                }
            }
            return bot + top >> 1;
        }
    }
}
