using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace McServerManager.Utilities;

public static class AuthenticodeVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool TryVerify(string filePath, out string? signerSubject, out string? errorMessage)
    {
        signerSubject = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            errorMessage = "署名検証対象ファイルが見つかりません。";
            return false;
        }

        if (!VerifyTrust(filePath, out var trustError))
        {
            errorMessage = trustError;
            return false;
        }

        signerSubject = TryGetSignerSubject(filePath);
        if (string.IsNullOrWhiteSpace(signerSubject))
        {
            errorMessage = "署名証明書の Subject を取得できませんでした。";
            return false;
        }

        return true;
    }

    private static bool VerifyTrust(string filePath, out string? errorMessage)
    {
        errorMessage = null;

        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPtr = IntPtr.Zero;
        var trustDataPtr = IntPtr.Zero;

        try
        {
            fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

            var trustData = new WinTrustData(fileInfoPtr)
            {
                DwStateAction = WinTrustDataStateAction.Verify,
                DwProvFlags = WinTrustDataProvFlags.RevocationCheckNone
            };

            trustDataPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPtr, fDeleteOld: false);

            var result = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, trustDataPtr);

            var closeData = Marshal.PtrToStructure<WinTrustData>(trustDataPtr);
            closeData.DwStateAction = WinTrustDataStateAction.Close;
            Marshal.StructureToPtr(closeData, trustDataPtr, fDeleteOld: true);
            _ = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, trustDataPtr);

            if (result != 0)
            {
                errorMessage = $"WinVerifyTrust failed: 0x{result:X8}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"署名検証中に例外が発生しました: {ex.Message}";
            return false;
        }
        finally
        {
            if (trustDataPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(trustDataPtr);
            }

            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPtr);
            }

            fileInfo.Dispose();
        }
    }

    private static string? TryGetSignerSubject(string filePath)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("wintrust.dll", PreserveSig = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        IntPtr pWinTrustData);

    private enum WinTrustDataUIChoice : uint
    {
        All = 1,
        None = 2,
        NoBad = 3,
        NoGood = 4
    }

    private enum WinTrustDataRevocationChecks : uint
    {
        None = 0x00000000,
        WholeChain = 0x00000001
    }

    private enum WinTrustDataChoice : uint
    {
        File = 1
    }

    private enum WinTrustDataStateAction : uint
    {
        Ignore = 0x00000000,
        Verify = 0x00000001,
        Close = 0x00000002
    }

    [Flags]
    private enum WinTrustDataProvFlags : uint
    {
        RevocationCheckNone = 0x00000010
    }

    private enum WinTrustDataUIContext : uint
    {
        Execute = 0
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo : IDisposable
    {
        public uint CbStruct;
        public IntPtr PcwszFilePath;
        public IntPtr HFile;
        public IntPtr PgKnownSubject;

        public WinTrustFileInfo(string fileName)
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            PcwszFilePath = Marshal.StringToCoTaskMemUni(fileName);
            HFile = IntPtr.Zero;
            PgKnownSubject = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (PcwszFilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(PcwszFilePath);
                PcwszFilePath = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PPolicyCallbackData;
        public IntPtr PSIPClientData;
        public WinTrustDataUIChoice DwUIChoice;
        public WinTrustDataRevocationChecks FdwRevocationChecks;
        public WinTrustDataChoice DwUnionChoice;
        public IntPtr PFile;
        public WinTrustDataStateAction DwStateAction;
        public IntPtr HWVTStateData;
        public IntPtr PwszURLReference;
        public WinTrustDataProvFlags DwProvFlags;
        public WinTrustDataUIContext DwUIContext;
        public IntPtr PSignatureSettings;

        public WinTrustData(IntPtr pFile)
        {
            CbStruct = (uint)Marshal.SizeOf<WinTrustData>();
            PPolicyCallbackData = IntPtr.Zero;
            PSIPClientData = IntPtr.Zero;
            DwUIChoice = WinTrustDataUIChoice.None;
            FdwRevocationChecks = WinTrustDataRevocationChecks.None;
            DwUnionChoice = WinTrustDataChoice.File;
            PFile = pFile;
            DwStateAction = WinTrustDataStateAction.Ignore;
            HWVTStateData = IntPtr.Zero;
            PwszURLReference = IntPtr.Zero;
            DwProvFlags = WinTrustDataProvFlags.RevocationCheckNone;
            DwUIContext = WinTrustDataUIContext.Execute;
            PSignatureSettings = IntPtr.Zero;
        }
    }
}
