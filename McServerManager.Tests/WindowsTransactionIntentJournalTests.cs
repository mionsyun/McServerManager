using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using McServerManager.Models.Transactions;
using McServerManager.Services.WindowsOwnership;
using McServerManager.Services.WindowsTransactions;
using Xunit.Abstractions;

namespace McServerManager.Tests;

public sealed class WindowsTransactionIntentJournalTests(ITestOutputHelper output)
{
    private static string Sandbox() => Path.Combine(Path.GetTempPath(), "maipilot-journal-" + Guid.NewGuid().ToString("N"));
    private static TransactionReceipt Receipt() => new(Guid.NewGuid(), Guid.NewGuid(), "synthetic-journal", new string('a', 64));
    private static string RecordPath(string root) => Path.Combine(root, WindowsTransactionIntentJournal.FileName);

    [Fact]
    public void FlushedIntentIsExactButLiveOwnerExcludesInspectionAndDuplicateRoot()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt()))
        {
            ticket = journal.Ticket;
            journal.WriteAndFlush();
            journal.WriteAndFlush(new CancellationToken(true)); // Acknowledged writes are idempotent.
            Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
            Assert.Throws<System.ComponentModel.Win32Exception>(() => WindowsTransactionIntentJournal.CreateNew(root, Receipt()));
            Assert.Throws<IOException>(() => Directory.Move(root, root + "-moved"));
        }
        var before = File.ReadAllBytes(RecordPath(root));
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.Equal(before, File.ReadAllBytes(RecordPath(root)));
        Assert.Throws<System.ComponentModel.Win32Exception>(() => WindowsTransactionIntentJournal.CreateNew(root, ticket.Receipt));
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData(0)] // Cancellation before the first write.
    [InlineData(1)] // Cancellation after a real partial write.
    [InlineData(2)] // Complete bytes in OS cache, no durable-write acknowledgement.
    [InlineData(3)] // Cancellation after acknowledged flush cannot revoke it.
    public void CancellationDistinguishesCurrentBytesFromFlushAcknowledgement(int checkpoint)
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using var cancellation = new CancellationTokenSource();
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt(), phase =>
               { if ((int)phase == checkpoint) cancellation.Cancel(); }))
        {
            ticket = journal.Ticket;
            if (checkpoint == 3) journal.WriteAndFlush(cancellation.Token);
            else
            {
                Assert.Throws<OperationCanceledException>(() => journal.WriteAndFlush(cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => journal.WriteAndFlush());
            }
        }
        Assert.Equal(checkpoint >= 2 ? WindowsIntentObservation.ExactIntentPresent : WindowsIntentObservation.Unknown,
            WindowsTransactionIntentJournal.Inspect(root, ticket));
        Directory.Delete(root, true);
    }

    [Fact]
    public void PreCancelledCallDoesNotStartAnAttempt()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt()))
        {
            ticket = journal.Ticket;
            Assert.Throws<OperationCanceledException>(() => journal.WriteAndFlush(new CancellationToken(true)));
            journal.WriteAndFlush();
        }
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Directory.Delete(root, true);
    }

    [Fact]
    public void TicketObservesSameObjectsAtNewPathButDoesNotAuthorizeAdoption()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        var moved = root + "-moved"; Directory.Move(root, moved);
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.False(Directory.Exists(root));
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(moved, ticket));
        Assert.Throws<System.ComponentModel.Win32Exception>(() => WindowsTransactionIntentJournal.CreateNew(moved, ticket.Receipt));
        Directory.Delete(moved, true);
    }

    [Fact]
    public void InterruptedWriteIsUnknownAndNeverRewritten()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt(), phase =>
               { if (phase == IntentWriteCheckpoint.AfterPartialWrite) throw new IOException("Injected interruption after real NTFS write."); }))
        {
            ticket = journal.Ticket;
            Assert.Throws<IOException>(() => journal.WriteAndFlush());
            Assert.Throws<InvalidOperationException>(() => journal.WriteAndFlush());
        }
        var partial = File.ReadAllBytes(RecordPath(root));
        Assert.NotEmpty(partial);
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.Equal(partial, File.ReadAllBytes(RecordPath(root)));
        Directory.Delete(root, true);
    }

    [Fact]
    public void ChangedReceiptBytesOrMissingJournalNeverAuthorizeRepair()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        var bytes = File.ReadAllBytes(RecordPath(root));
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket with { Receipt = ticket.Receipt with { OwnerToken = Guid.NewGuid() } }));
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket with { Receipt = ticket.Receipt with { PlanFingerprint = new string('b', 64) } }));
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket with { Version = 2 }));
        bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(RecordPath(root), bytes);
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.Equal(bytes, File.ReadAllBytes(RecordPath(root)));
        File.Delete(RecordPath(root));
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.False(File.Exists(RecordPath(root)));
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("journal")]
    [InlineData("reservation")]
    [InlineData("root")]
    public void SameBytesWithDifferentNativeIdentityRemainUnknown(string replaced)
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        if (replaced == "root")
        {
            Directory.Move(root, root + "-retired"); Directory.CreateDirectory(root);
            foreach (var file in Directory.GetFiles(root + "-retired")) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }
        else
        {
            var path = replaced == "journal" ? RecordPath(root) : Path.Combine(root, WindowsOwnedDirectory.ReservationName);
            File.Move(path, path + ".retired"); File.Copy(path + ".retired", path);
        }
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.True(File.Exists(RecordPath(root)));
        Directory.Delete(root, true);
        if (replaced == "root") Directory.Delete(root + "-retired", true);
    }

    [Fact]
    public void HardlinkedJournalIsRejectedWithoutTouchingEitherName()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        var link = Path.Combine(root, "second-name.bin");
        if (!CreateHardLinkW(link, RecordPath(root), IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var bytes = File.ReadAllBytes(link);
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.Equal(bytes, File.ReadAllBytes(link)); Assert.Equal(bytes, File.ReadAllBytes(RecordPath(root)));
        Directory.Delete(root, true);
    }

    [Fact]
    public void HardlinkedReservationIsRejectedWithoutChangingEitherNameOrJournal()
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        var reservation = Path.Combine(root, WindowsOwnedDirectory.ReservationName);
        var link = Path.Combine(root, "reservation-second-name.bin");
        var reservationBytes = File.ReadAllBytes(reservation);
        var journalBytes = File.ReadAllBytes(RecordPath(root));
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        if (!CreateHardLinkW(link, reservation, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.Equal(reservationBytes, File.ReadAllBytes(reservation));
        Assert.Equal(reservationBytes, File.ReadAllBytes(link));
        Assert.Equal(journalBytes, File.ReadAllBytes(RecordPath(root)));
        File.Delete(link); // Fixture-only removal restores the single-link positive control.
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingOrOversizedJournalIsUnknownAndInspectionDoesNotTruncate(bool oversized)
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        var path = RecordPath(root);
        var original = File.ReadAllBytes(path);
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        using (var append = new FileStream(path, FileMode.Append, FileAccess.Write))
            append.Write(new byte[oversized ? 4097 - original.Length : 1]);
        var changed = File.ReadAllBytes(path);
        Assert.Equal(oversized ? 4097 : original.Length + 1, changed.Length);
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Assert.Equal(changed, File.ReadAllBytes(path));
        using (var restore = new FileStream(path, FileMode.Open, FileAccess.Write)) restore.SetLength(original.Length);
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("null-ticket")]
    [InlineData("null-receipt")]
    [InlineData("null-root")]
    [InlineData("null-reservation")]
    [InlineData("null-journal")]
    [InlineData("version")]
    [InlineData("empty-install")]
    [InlineData("empty-owner")]
    [InlineData("invalid-storage")]
    [InlineData("invalid-fingerprint")]
    public void InvalidTicketIsUnknownWithoutChangingExistingFilesOrCreatingMissingRoot(string invalid)
    {
        var root = Sandbox(); WindowsIntentTicket ticket;
        using (var journal = WindowsTransactionIntentJournal.CreateNew(root, Receipt())) { ticket = journal.Ticket; journal.WriteAndFlush(); }
        WindowsIntentTicket malformed = invalid switch
        {
            "null-ticket" => null!,
            "null-receipt" => ticket with { Receipt = null! },
            "null-root" => ticket with { Root = null! },
            "null-reservation" => ticket with { Reservation = null! },
            "null-journal" => ticket with { Journal = null! },
            "version" => ticket with { Version = -1 },
            "empty-install" => ticket with { Receipt = ticket.Receipt with { InstallId = Guid.Empty } },
            "empty-owner" => ticket with { Receipt = ticket.Receipt with { OwnerToken = Guid.Empty } },
            "invalid-storage" => ticket with { Receipt = ticket.Receipt with { StorageId = "../invalid" } },
            "invalid-fingerprint" => ticket with { Receipt = ticket.Receipt with { PlanFingerprint = new string('A', 64) } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var paths = Directory.GetFiles(root).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var before = paths.Select(File.ReadAllBytes).ToArray();
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(root, malformed));
        Assert.Equal(paths, Directory.GetFiles(root).OrderBy(path => path, StringComparer.Ordinal).ToArray());
        for (var i = 0; i < paths.Length; i++) Assert.Equal(before[i], File.ReadAllBytes(paths[i]));
        var missing = root + "-missing";
        Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(missing, malformed));
        Assert.False(Directory.Exists(missing));
        Assert.Equal(WindowsIntentObservation.ExactIntentPresent, WindowsTransactionIntentJournal.Inspect(root, ticket));
        Directory.Delete(root, true);
    }

    [Fact]
    public void InvalidReceiptRejectsBeforeCreationAndNoGlobalInstallIdClaimIsMade()
    {
        var root = Sandbox();
        Assert.Throws<ArgumentException>(() => WindowsTransactionIntentJournal.CreateNew(root, Receipt() with { InstallId = Guid.Empty }));
        Assert.False(Directory.Exists(root));
        var receipt = Receipt();
        using (var first = WindowsTransactionIntentJournal.CreateNew(root, receipt))
        using (var second = WindowsTransactionIntentJournal.CreateNew(root + "-second", receipt))
        { first.WriteAndFlush(); second.WriteAndFlush(); }
        Directory.Delete(root, true); Directory.Delete(root + "-second", true);
    }

    [Theory]
    [InlineData("BeforeWrite", false)]
    [InlineData("AfterPartialWrite", false)]
    [InlineData("AfterCompleteWrite", true)]
    [InlineData("AfterFlush", true)]
    public async Task ActualProcessCrashHasReadOnlyBoundedReconciliation(string phase, bool exactBytes)
    {
        var root = Sandbox(); Directory.CreateDirectory(root);
        using (var containment = new WindowsOwnedJob())
        {
            var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            var child = Path.Combine(AppContext.BaseDirectory, "OwnershipTestChild", "McServerManager.OwnershipTestChild.dll");
            containment.Start(dotnet, [child, "journal-crash", root, phase], root, CancellationToken.None);
            await WaitUntil(() => File.Exists(Path.Combine(root, "checkpoint-ready")));
            var ticket = JsonSerializer.Deserialize<WindowsIntentTicket>(await File.ReadAllTextAsync(Path.Combine(root, "ticket.json")))!;
            var directory = Path.Combine(root, "journal");
            Assert.Equal(WindowsIntentObservation.Unknown, WindowsTransactionIntentJournal.Inspect(directory, ticket));
            await File.WriteAllTextAsync(Path.Combine(root, "crash-now"), "terminate fixture only");
            await WaitUntil(() => containment.IsEmpty);
            var before = File.ReadAllBytes(RecordPath(directory));
            var observed = WindowsTransactionIntentJournal.Inspect(directory, ticket);
            Assert.Equal(exactBytes ? WindowsIntentObservation.ExactIntentPresent : WindowsIntentObservation.Unknown, observed);
            Assert.Equal(before, File.ReadAllBytes(RecordPath(directory)));
            Assert.Throws<System.ComponentModel.Win32Exception>(() => WindowsTransactionIntentJournal.CreateNew(directory, ticket.Receipt));
            output.WriteLine($"Crash checkpoint={phase}; current observation={observed}; prior flush success is not inferred; bytes unchanged.");
        }
        Directory.Delete(root, true);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Journal fixture boundary was not reached.");
            await Task.Delay(20);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr security);
}
