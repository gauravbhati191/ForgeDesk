using System.Text;
using ForgeDesk.Application.Logging;

namespace ForgeDesk.UnitTests;

public sealed class FileLogRetentionTests
{
    [Theory]
    [InlineData(0, 10, 10, false)]
    [InlineData(1, 9, 10, false)]
    [InlineData(1, 10, 10, true)]
    [InlineData(11, 1, 10, true)]
    public void Rotation_is_requested_only_when_the_next_entry_would_exceed_the_limit(
        long currentLength,
        long incomingLength,
        long maximumLength,
        bool expected)
    {
        Assert.Equal(expected, FileLogRetention.ShouldRotate(currentLength, incomingLength, maximumLength));
    }

    [Fact]
    public void Rotation_shifts_backups_and_removes_generations_outside_retention()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "forgedesk.log");
        File.WriteAllText(path, "active", Encoding.UTF8);
        File.WriteAllText(FileLogRetention.GetBackupPath(path, 1), "first", Encoding.UTF8);
        File.WriteAllText(FileLogRetention.GetBackupPath(path, 2), "second", Encoding.UTF8);
        File.WriteAllText(FileLogRetention.GetBackupPath(path, 3), "stale", Encoding.UTF8);

        Assert.True(FileLogRetention.TryRotate(path, retainedFileCount: 2));

        Assert.False(File.Exists(path));
        Assert.Equal("active", File.ReadAllText(FileLogRetention.GetBackupPath(path, 1)));
        Assert.Equal("first", File.ReadAllText(FileLogRetention.GetBackupPath(path, 2)));
        Assert.False(File.Exists(FileLogRetention.GetBackupPath(path, 3)));
    }

    [Fact]
    public void Oversized_unicode_entries_are_truncated_at_valid_utf8_boundaries()
    {
        var entry = FileLogRetention.EncodeBoundedUtf8("prefix-😀-suffix", maxBytes: 10);

        Assert.True(entry.Length <= 10);
        var decoded = Encoding.UTF8.GetString(entry);
        Assert.DoesNotContain("�", decoded, StringComparison.Ordinal);
        Assert.StartsWith("prefix-", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotation_failure_is_reported_as_a_result_without_throwing()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "missing", "forgedesk.log");

        var result = FileLogRetention.TryRotate(path, retainedFileCount: 2);

        Assert.False(result);
    }
}
