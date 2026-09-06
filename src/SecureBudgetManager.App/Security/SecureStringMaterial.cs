using System.Runtime.InteropServices;
using System.Security;

namespace SecureBudgetManager.App.Security;

/// <summary>
/// Converts a PasswordBox secret into a char array that the vault can clear.
///
/// WPF's PasswordBox keeps its own copy of the text in memory, so this reduces rather than
/// removes exposure. The array handed to the vault is always zeroed by the vault.
/// </summary>
internal static class SecureStringMaterial
{
    public static char[] ToCharArray(SecureString? secureString)
    {
        if (secureString is null || secureString.Length == 0)
        {
            return [];
        }

        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            var buffer = GC.AllocateArray<char>(secureString.Length, pinned: true);

            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (char)Marshal.ReadInt16(pointer, i * sizeof(char));
            }

            return buffer;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            }
        }
    }
}
