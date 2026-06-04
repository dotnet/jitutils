using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Antigen
{
    public class Weights<T>
    {
        public double Weight;
        public T Data;

        public Weights(T data_, double Weight_)
        {
            Data = data_;
            Weight = Weight_;
        }

        public override string ToString()
        {
            return $"{Data.ToString()} : {Weight.ToString()}";
        }
    }
#pragma warning disable SYSLIB0023
    public class PRNG
    {
        private static System.Security.Cryptography.RNGCryptoServiceProvider SecureRand;
        private static Dictionary<int, Random> PerThreadRand = new Dictionary<int, Random>();
        private static Dictionary<int, int> PerThreadSeed = new Dictionary<int, int>();
        private static bool isFixSeed = false;

        static public void Initialize(int seed)
        {
            if (seed == -1)
            {
                isFixSeed = false;
                SecureRand = new System.Security.Cryptography.RNGCryptoServiceProvider();
            }
            else
            {
                isFixSeed = true;
                lock (PerThreadRand)
                {
                    int currentThreadId = Thread.CurrentThread.ManagedThreadId;
                    int currentSeed = seed + (/*Program.RunOptions.NumThreads*/ 5 * PerThreadRand.Count); // every thread gets seed value in multiples of 100s
                    if (!PerThreadRand.ContainsKey(currentThreadId))
                    {
                        PerThreadRand[currentThreadId] = new Random(currentSeed);
                        PerThreadSeed[currentThreadId] = currentSeed;
                    }
                }
            }
        }

        static public int GetSeed()
        {
            int seedValue = -1;
            if (isFixSeed)
            {
                int currentThreadId = Thread.CurrentThread.ManagedThreadId;

                if (PerThreadSeed.ContainsKey(currentThreadId))
                {
                    seedValue = PerThreadSeed[currentThreadId];
                }
            }
            return seedValue;
        }

        static public int Next(int max)
        {
            return Next(0, max);
        }

        static public int Next(int min, int max)
        {
            int ret;

            if (min > max)
            {
                int temp = max;
                max = min;
                min = temp;
            }

            if (isFixSeed)
            {
                ret = PerThreadRand[Thread.CurrentThread.ManagedThreadId].Next(min, max);
            }
            else
            {
                //if sample set is 0 then just return 0 
                if (max == 0)
                {
                    ret = 0;
                }
                else
                {
                    Debug.Assert(min != max, "min = max in PRNG.Next()");

                    Byte[] data = new Byte[4];
                    SecureRand.GetBytes(data);

                    // We don't want the high bit set, since it has to be a positive number.
                    data[3] = (byte)((int)data[3] & 0x7F);

                    ret = (data[0] + (data[1] << 8) + (data[2] << 16) + (data[3] << 24)) % max;
                    // If value is less than adjust it to be higer than min
                    if (ret < min)
                    {
                        ret = min + (ret % (max - min));
                    }
                }
            }

            return ret;
        }

        static public long NextLong(long max)
        {
            long ret = 0;

            if (isFixSeed)
            {
                //if sample set is 0 then just return 0 
                if (max == 0)
                {
                    ret = 0;
                }
                else
                {
                    int shift = 0;
                    for (int i = 0; i < 4; ++i)
                    {
                        long data = (long)PerThreadRand[Thread.CurrentThread.ManagedThreadId].Next(65536);
                        ret += data << shift;
                        shift += 16;
                    }
                    ret %= max;
                }
            }
            else
            {
                //if sample set is 0 then just return 0 
                if (max == 0)
                {
                    ret = 0;
                }
                else
                {
                    Byte[] origData = new Byte[8];
                    SecureRand.GetBytes(origData);

                    long[] data = new long[8];
                    for (int i = 0; i < 8; ++i)
                        data[i] = (long)origData[i];

                    ret = (long)(data[0] + (data[1] << 8) + (data[2] << 16) + (data[3] << 24)
                        + (data[4] << 32) + (data[5] << 40) + (data[6] << 48) + (data[7] << 56)) % max;
                }
            }

            return ret;
        }

        /// <summary>
        /// Returns number between 0 and max (inclusive) based on exponential function i.e. 
        /// lowest value (1) gets higest weightage followed by 2, ..., max.
        /// </summary>
        /// <param name="max"></param>
        /// <returns></returns>
        public static int NextWithExponentialFactor(int max, double exponentialFactor, int min = 0)
        {
            if (max == 0)
                return 0;

            Debug.Assert(min < max, "min < max inside PRNG.NextExponential()");
            List<Weights<int>> choices = new List<Weights<int>>();
            int N = min;
            while (max >= min)
            {
                double weight = Math.Pow(max, exponentialFactor);
                // if the weight is too high because max/exponentialFactor was very huge number, then there is less chance that lower
                // numbers will be selected, so just return one of the first 3 values.
                if(double.IsNaN(weight) || double.IsInfinity(weight) || double.IsPositiveInfinity(weight) || double.IsNegativeInfinity(weight)) 
                {
                    return PRNG.Next(min, min + 3);
                }
                choices.Add(new Weights<int>(N++, weight));
                max--;
            }
            return WeightedChoice<int>(choices);
        }

        /// <summary>
        /// Decide - takes a %weight which is between 0-1.
        /// </summary>
        /// <param name="weight">%</param>
        /// <returns></returns>
        public static bool Decide(double weight)
        {
            Debug.Assert(weight >= 0 && weight <= 1);
            return PRNG.Next(Int32.MaxValue) <= weight * Int32.MaxValue;
        }

        public static T WeightedChoice<T>(IEnumerable<Weights<T>> choices)
        {
            // Sum up all of the weights
            double sum = (from z in choices
                          select z.Weight).Sum();

            // Create a normalized random value
            double rand = Next(65536) * sum / 65536.0;

            double prev = 0;
            double currMax = 0;
            foreach(Weights<T> curr in choices)
            {
                prev = currMax;
                currMax += curr.Weight;
                if (rand >= prev && rand < currMax)
                {
                    return curr.Data;
                }
            }
            System.Diagnostics.Debug.Assert(false, "Should never get here!");
            return default(T);
        }
    };
#pragma warning restore SYSLIB0023
}
