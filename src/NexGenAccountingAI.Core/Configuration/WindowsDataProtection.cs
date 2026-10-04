using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NexGenAccountingAI.Core.Configuration;

internal static class WindowsDataProtection
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Accounting AI configuration v2"); // não renomear: a configuração já cifrada ficaria ilegível

    public static byte[] Protect(byte[] plainText) => Transform(plainText, protect: true);

    public static byte[] Unprotect(byte[] cipherText) => Transform(cipherText, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("A configuração encriptada do NexGen Accounting AI requer Windows.");
        }

        var inputBlob = CreateBlob(input);
        var entropyBlob = CreateBlob(OptionalEntropy);
        try
        {
            var success = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);
            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível proteger a configuração com o Windows.");
            }

            try
            {
                var output = new byte[outputBlob.Size];
                if (output.Length > 0)
                {
                    Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                }

                return output;
            }
            finally
            {
                if (outputBlob.Data != IntPtr.Zero)
                {
                    LocalFree(outputBlob.Data);
                }
            }
        }
        finally
        {
            FreeBlob(inputBlob);
            FreeBlob(entropyBlob);
        }
    }

    private static DataBlob CreateBlob(byte[] value)
    {
        if (value.Length == 0)
        {
            return new DataBlob();
        }

        var data = Marshal.AllocHGlobal(value.Length);
        Marshal.Copy(value, 0, data, value.Length);
        return new DataBlob { Size = value.Length, Data = data };
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        int flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        int flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
