using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static McServerManager.Services.WindowsOwnership.WindowsOwnershipNative;

namespace McServerManager.Services.WindowsOwnership;

internal sealed class WindowsOwnedDirectory : IDisposable
{
    internal const string ReservationName = ".maipilot-owned-runtime";
    private readonly List<(SafeFileHandle Handle, FileIdentity Identity, string Path)> _pins = new();
    private FileStream? _reservation;
    internal string Path { get; }

    private WindowsOwnedDirectory(string path) => Path = path;

    internal static WindowsOwnedDirectory CreateNew(string path)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("Windows 10 or later is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var canonical = System.IO.Path.GetFullPath(path);
        if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
            !string.Equals(canonical, path, StringComparison.OrdinalIgnoreCase) || path.EndsWith('\\'))
            throw new ArgumentException("A canonical local drive path is required.", nameof(path));
        var components = path[3..].Split('\\');
        foreach (var component in components)
        {
            var stem = component.Split('.')[0];
            if (component.Length == 0 || component.EndsWith('.') || component.EndsWith(' ') || component.Contains(':') ||
                component.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
                new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsDigit(stem[3])))
                throw new ArgumentException("Ambiguous path component.", nameof(path));
        }
        var drive = new DriveInfo(path[..3]);
        if (drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("This experimental owner requires a local fixed NTFS volume.");
        var owner = new WindowsOwnedDirectory(path);
        try
        {
            var current = path[..3];
            owner.Pin(current);
            for (var index = 0; index < components.Length - 1; index++)
            {
                current = System.IO.Path.Combine(current, components[index]);
                owner.Pin(current);
            }
            // Never adopt an existing directory, including abandoned owners. No crash recovery is implied.
            if (!CreateDirectoryW(path, IntPtr.Zero)) throw Error();
            owner.Pin(path);
            var reservation = CreateFileW(System.IO.Path.Combine(path, ReservationName), 0xC0000000, 0, IntPtr.Zero, 1, OpenReparse, IntPtr.Zero);
            if (reservation.IsInvalid) { reservation.Dispose(); throw Error(); }
            owner._reservation = new FileStream(reservation, FileAccess.ReadWrite);
            owner._reservation.Write(Encoding.UTF8.GetBytes("MaiPilot experimental owner; never adopt this directory.\n"));
            owner._reservation.Flush(true);
            owner.Validate();
            return owner;
        }
        catch { owner.Dispose(); throw; } // Newly created artifacts intentionally remain ineligible for adoption.
    }

    private void Pin(string path)
    {
        // Retained pins deny rename/delete and incompatible data-write opens.
        // Sharing alone is not a general lock against attribute/reparse-point changes.
        var handle = CreateFileW(path, 0x80, 1, IntPtr.Zero, 3, BackupSemantics | OpenReparse, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new System.ComponentModel.Win32Exception(code, "Cannot pin directory: " + path);
        }
        try
        {
            var identity = Inspect(handle);
            var final = new StringBuilder(32768);
            var length = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
            if (length == 0) throw Error();
            if (length >= final.Capacity || !string.Equals(final.ToString(), "\\\\?\\" + path, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Directory aliases are unsupported.");
            _pins.Add((handle, identity, path));
        }
        catch { handle.Dispose(); throw; }
    }

    private static FileIdentity Inspect(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTag attributes, (uint)Marshal.SizeOf<AttributeTag>())) throw Error();
        if ((attributes.Attributes & 0x400) != 0 || (attributes.Attributes & 0x10) == 0) throw new IOException("A real non-reparse directory is required.");
        if (!GetFileInformationByHandleEx(handle, 18, out FileIdentity identity, (uint)Marshal.SizeOf<FileIdentity>())) throw Error();
        return identity;
    }

    internal void Validate()
    {
        foreach (var pin in _pins)
        {
            var current = Inspect(pin.Handle);
            if (current.Volume != pin.Identity.Volume || current.Low != pin.Identity.Low || current.High != pin.Identity.High)
                throw new IOException("Pinned directory identity changed.");
        }
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        for (var index = _pins.Count - 1; index >= 0; index--) _pins[index].Handle.Dispose();
        _pins.Clear();
    }
}
