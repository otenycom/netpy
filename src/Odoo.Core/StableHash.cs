namespace Odoo.Core
{
    /// <summary>
    /// Provides deterministic hash codes for stable tokens across compilations/runs.
    /// Uses a simple polynomial hash that produces consistent results regardless of
    /// platform or .NET version.
    /// </summary>
    public static class StableHash
    {
        /// <summary>
        /// Computes a deterministic hash code for a string.
        /// This hash is stable across compilations and runs, making it suitable
        /// for tokens that need to be consistent between source generation and runtime.
        /// </summary>
        /// <param name="str">The string to hash.</param>
        /// <returns>A stable 64-bit hash code.</returns>
        public static long GetStableHashCode(string str)
        {
            unchecked
            {
                long hash = 23;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}
