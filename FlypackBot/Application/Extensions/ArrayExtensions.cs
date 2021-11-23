namespace FlypackBot.Application.Extensions
{
    public static class ArrayExtensions
    {
        public static T[] Merge<T>(this T[] left, T[] right)
        {
            var mergedArrays = new T[left.Length + right.Length];

            for (int i = 0; i < left.Length; i++)
                mergedArrays[i] = (left[i]);

            for (int i = 0; i < right.Length; i++)
                mergedArrays[i + left.Length] = right[i];

            return mergedArrays;
        }
    }
}
