using System;

namespace DHA.DSTC.Utilities
{
    /// <summary>
    /// Utility class for converting between GUIDs and integer IDs
    /// </summary>
    public static class GuidConverter
    {
        /// <summary>
        /// Attempts to convert a Guid to an integer for services that require int IDs
        /// </summary>
        /// <param name="guid">The Guid to convert</param>
        /// <param name="intId">The resulting integer ID</param>
        /// <returns>True if conversion was successful, false otherwise</returns>
        public static bool TryConvertGuidToInt(Guid guid, out int intId)
        {
            // Try direct conversion first (this might work for some small Guids)
            if (int.TryParse(guid.ToString("N").Substring(0, 8),
                    System.Globalization.NumberStyles.HexNumber, null, out intId))
            {
                return true;
            }

            // If that fails, use a hash code approach
            try
            {
                intId = guid.GetHashCode();
                return true;
            }
            catch
            {
                // Last resort: use a random positive number
                intId = new Random().Next(1, int.MaxValue);
                return true;
            }
        }

        public static Guid GetDeterministicGuidFromId(int id)
        {
            // This is a simple approach - you might need something more sophisticated
            // based on how your actual data is stored
            string idStr = id.ToString().PadLeft(32, '0');
            try
            {
                return new Guid(idStr.Substring(0, 8) + "-" + idStr.Substring(8, 4) + "-" +
                               idStr.Substring(12, 4) + "-" + idStr.Substring(16, 4) + "-" +
                               idStr.Substring(20, 12));
            }
            catch
            {
                // Fallback to a deterministic approach
                byte[] bytes = new byte[16];
                BitConverter.GetBytes(id).CopyTo(bytes, 0);
                return new Guid(bytes);
            }
        }

        /// <summary>
        /// Converts a Guid to an integer, using a default value if conversion fails
        /// </summary>
        /// <param name="guid">The Guid to convert</param>
        /// <param name="defaultValue">Default value to use if conversion fails</param>
        /// <returns>The converted integer or default value</returns>
        public static int GuidToInt(Guid guid, int defaultValue = 0)
        {
            int result;
            if (TryConvertGuidToInt(guid, out result))
            {
                return result;
            }

            return defaultValue;
        }

        /// <summary>
        /// Attempts to create a deterministic integer ID from a Guid
        /// that will be consistent across application restarts
        /// </summary>
        /// <param name="guid">The Guid to convert</param>
        /// <returns>A deterministic integer ID</returns>
        public static int GetDeterministicIntId(Guid guid)
        {
            // Convert to string and take first 8 chars for consistent hash
            string guidStr = guid.ToString("N");

            // Simple hash function
            int hash = 0;
            for (int i = 0; i < guidStr.Length; i++)
            {
                hash = (hash * 31) + guidStr[i];
            }

            // Ensure positive value
            return Math.Abs(hash);
        }
    }
}