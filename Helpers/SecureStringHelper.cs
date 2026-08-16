using System.Runtime.InteropServices;
using System.Security;

namespace Sati.Helpers
{
    /// <summary>
    /// Stateless SecureString comparison shared by every "type it twice" password field.
    /// Single source of truth for confirmation matching — parallel to WorkdayHelper.
    ///
    /// Exists because comparing SecureString.Length is the obvious shortcut and is wrong:
    /// it passes any two different passwords of equal length, so a typo in the confirm box
    /// silently creates an account whose password is not what the user believes it to be.
    /// </summary>
    public static class SecureStringHelper
    {
        /// <summary>
        /// True when both SecureStrings hold identical contents. Null never matches,
        /// including null against null — a caller with no password has nothing to confirm.
        /// </summary>
        public static bool Matches(SecureString? left, SecureString? right)
        {
            if (left is null || right is null)
                return false;

            if (left.Length != right.Length)
                return false;

            // Both are decrypted into unmanaged memory only for the length of the
            // comparison, and zeroed in the finally regardless of how it exits.
            var leftPtr = IntPtr.Zero;
            var rightPtr = IntPtr.Zero;
            try
            {
                leftPtr = Marshal.SecureStringToGlobalAllocUnicode(left);
                rightPtr = Marshal.SecureStringToGlobalAllocUnicode(right);

                // Unicode: two bytes per character, so walk the buffer in 2-byte steps.
                for (var i = 0; i < left.Length * 2; i += 2)
                {
                    if (Marshal.ReadInt16(leftPtr, i) != Marshal.ReadInt16(rightPtr, i))
                        return false;
                }

                return true;
            }
            finally
            {
                if (leftPtr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(leftPtr);
                if (rightPtr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(rightPtr);
            }
        }
    }
}
