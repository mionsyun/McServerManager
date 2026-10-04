using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McServerManager.Models.Transactions;
using McServerManager.Services.Transactions;
using McServerManager.Services.WindowsOwnership;
using Microsoft.Win32.SafeHandles;
using static McServerManager.Services.WindowsOwnership.WindowsOwnershipNative;

namespace McServerManager.Services.WindowsTransactions;

public sealed record WindowsIntentFileIdentity(ulong Volume, ulong Low, ulong High);

/// <summary>Read-only inspection context, not authentication, flush acknowledgement, or write/delete authority.</summary>
public sealed record WindowsIntentTicket(int Version, TransactionReceipt Receipt, WindowsIntentFileIdentity Root,
    WindowsIntentFileIdentity Reservation, WindowsIntentFileIdentity Journal);

/// <summary>Current exact bytes only. Neither value is a transaction commit, staged-tree proof, or durable-write acknowledgement.</summary>
public enum WindowsIntentObservation { Unknown, ExactIntentPresent }

internal enum IntentWriteCheckpoint { BeforeWrite, AfterPartialWrite, AfterCompleteWrite, AfterFlush }

/// <summary>
/// One create-only intent record in a newly owned isolated directory. This is deliberately NOT ITransactionStorage.
/// There is no publication, cleanup, mutable adoption, global installId reservation, or production registration.
/// </summary>
public sealed class WindowsTransactionIntentJournal : IDisposable
{
    internal const string FileName = "transaction-intent.bin";
    private const int MaximumRecordBytes = 4096;
    private readonly object _gate = new();
    private readonly WindowsOwnedDirectory _directory;
    private readonly FileStream _file;
    private readonly byte[] _record;
    private readonly Action<IntentWriteCheckpoint>? _checkpoint;
    private bool _attempted, _flushed, _disposed;
    public WindowsIntentTicket Ticket { get; }

    private WindowsTransactionIntentJournal(WindowsOwnedDirectory directory, FileStream file, TransactionReceipt receipt,
        Action<IntentWriteCheckpoint>? checkpoint)
    {
        _directory = directory; _file = file; _checkpoint = checkpoint;
        Ticket = new(1, receipt, Export(directory.RootIdentity), Export(directory.ReservationIdentity), Export(InspectRegularFile(file.SafeFileHandle)));
        _record = Encode(Ticket);
    }

    public static WindowsTransactionIntentJournal CreateNew(string directory, TransactionReceipt receipt) => CreateNew(directory, receipt, null);

    internal static WindowsTransactionIntentJournal CreateNew(string directory, TransactionReceipt receipt, Action<IntentWriteCheckpoint>? checkpoint)
    {
        ValidateReceipt(receipt); // Reject invalid authority before any filesystem mutation.
        var owner = WindowsOwnedDirectory.CreateNew(directory);
        FileStream? file = null;
        try
        {
            file = OpenFile(System.IO.Path.Combine(directory, FileName), create: true);
            return new WindowsTransactionIntentJournal(owner, file, receipt, checkpoint);
        }
        catch { file?.Dispose(); owner.Dispose(); throw; } // Preserve leftovers; creation failure never grants cleanup authority.
    }

    /// <summary>
    /// Successful return acknowledges Flush(true). Cancellation before the flush leaves an uncertain attempt;
    /// cancellation after it does not revoke the acknowledgement. A failed attempt is never rewritten or resumed.
    /// </summary>
    public void WriteAndFlush(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_flushed) return;
            cancellationToken.ThrowIfCancellationRequested();
            if (_attempted) throw new InvalidOperationException("An uncertain intent record cannot be rewritten.");
            _directory.Validate();
            RequireIdentity(InspectRegularFile(_file.SafeFileHandle), Ticket.Journal);
            if (_file.Length != 0) throw new IOException("The newly owned journal is no longer empty.");
            _attempted = true;
            _checkpoint?.Invoke(IntentWriteCheckpoint.BeforeWrite);
            cancellationToken.ThrowIfCancellationRequested();
            var half = _record.Length / 2;
            _file.Write(_record.AsSpan(0, half));
            _file.Flush();
            _checkpoint?.Invoke(IntentWriteCheckpoint.AfterPartialWrite);
            cancellationToken.ThrowIfCancellationRequested();
            _file.Write(_record.AsSpan(half));
            _file.Flush();
            _checkpoint?.Invoke(IntentWriteCheckpoint.AfterCompleteWrite);
            cancellationToken.ThrowIfCancellationRequested();
            _file.Flush(flushToDisk: true);
            _flushed = true;
            _checkpoint?.Invoke(IntentWriteCheckpoint.AfterFlush);
        }
    }

    /// <summary>
    /// Reopens only existing objects, holds reservation exclusion, and compares bounded exact bytes and identities.
    /// Complete bytes may survive an UNACKNOWLEDGED process crash via OS cache. This result cannot prove a prior flush.
    /// Missing, busy, changed, linked or incomplete records remain Unknown; inspection never repairs or deletes.
    /// </summary>
    public static WindowsIntentObservation Inspect(string directory, WindowsIntentTicket ticket)
    {
        try
        {
            var expected = Encode(ticket);
            using var owner = WindowsOwnedDirectory.OpenExistingForReadOnlyInspection(directory, Import(ticket.Root), Import(ticket.Reservation));
            using var file = OpenFile(System.IO.Path.Combine(directory, FileName), create: false);
            RequireIdentity(InspectRegularFile(file.SafeFileHandle), ticket.Journal);
            if (file.Length != expected.Length || file.Length > MaximumRecordBytes) return WindowsIntentObservation.Unknown;
            var actual = new byte[expected.Length];
            file.ReadExactly(actual);
            if (file.ReadByte() != -1 || !actual.AsSpan().SequenceEqual(expected)) return WindowsIntentObservation.Unknown;
            owner.Validate();
            RequireIdentity(owner.ReservationIdentity, ticket.Reservation);
            RequireIdentity(InspectRegularFile(file.SafeFileHandle), ticket.Journal);
            return WindowsIntentObservation.ExactIntentPresent;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or JsonException)
        { return WindowsIntentObservation.Unknown; }
    }

    private static FileStream OpenFile(string path, bool create)
    {
        var handle = CreateFileW(path, create ? 0xC0000000u : 0x80000000u, 0, IntPtr.Zero, create ? 1u : 3u, OpenReparse, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Error(); handle.Dispose(); throw error; }
        try
        {
            InspectRegularFile(handle);
            return new FileStream(handle, create ? FileAccess.ReadWrite : FileAccess.Read, bufferSize: 1, isAsync: false);
        }
        catch { handle.Dispose(); throw; }
    }

    private static byte[] Encode(WindowsIntentTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (ticket.Version != 1) throw new ArgumentException("Unsupported intent record version.");
        ValidateReceipt(ticket.Receipt);
        ArgumentNullException.ThrowIfNull(ticket.Root);
        ArgumentNullException.ThrowIfNull(ticket.Reservation);
        ArgumentNullException.ThrowIfNull(ticket.Journal);
        var payload = JsonSerializer.SerializeToUtf8Bytes(ticket);
        var record = Encoding.UTF8.GetBytes("MAIPILOT-INTENT-1\n" + Encoding.UTF8.GetString(payload) + "\n" +
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant() + "\n");
        if (record.Length > MaximumRecordBytes) throw new ArgumentException("Intent record exceeds the size limit.");
        return record;
    }

    private static void ValidateReceipt(TransactionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.InstallId == Guid.Empty || receipt.OwnerToken == Guid.Empty) throw new ArgumentException("Nonempty ownership identities are required.");
        TransactionPolicy.ValidateIdentity(receipt.StorageId);
        TransactionPolicy.ValidateSha256(receipt.PlanFingerprint);
    }

    private static WindowsIntentFileIdentity Export(FileIdentity identity) => new(identity.Volume, identity.Low, identity.High);
    private static FileIdentity Import(WindowsIntentFileIdentity identity) => new() { Volume = identity.Volume, Low = identity.Low, High = identity.High };
    private static void RequireIdentity(FileIdentity actual, WindowsIntentFileIdentity expected)
    {
        if (!SameIdentity(actual, Import(expected))) throw new IOException("Native file identity differs from the inspection ticket.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _file.Dispose(); _directory.Dispose(); _disposed = true;
        }
    }
}
