using System;
using System.IO;
using System.Linq;

internal static class MigrationTests
{
    private static int assertions;
    private static void Check(bool result, string name) { if (!result) throw new Exception(name); assertions++; }
    private static void Main()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "build-check", "migration-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(root);
        string old = Path.Combine(root, "old portable folder"), target = Path.Combine(root, "new data");
        Directory.CreateDirectory(old);
        foreach (string name in UserDataService.Names) File.WriteAllText(Path.Combine(old, name), "synthetic:" + name);
        Directory.CreateDirectory(Path.Combine(old, "backups", "nested"));
        File.WriteAllText(Path.Combine(old, "backups", "nested", "test.bak"), "synthetic backup");
        UserDataService.Migrate(target, new[] { old });
        foreach (string name in UserDataService.Names)
        {
            Check(File.ReadAllText(Path.Combine(old, name)) == File.ReadAllText(Path.Combine(target, name)), "migration " + name);
            Check(Directory.GetFiles(Path.Combine(target, "migration-backups"), name, SearchOption.AllDirectories).Any(), "backup " + name);
        }
        Check(File.ReadAllText(Path.Combine(target, "backups", "nested", "test.bak")) == "synthetic backup", "nested backup");
        File.WriteAllText(Path.Combine(target, "presets.json"), "new user data");
        int count = Directory.GetDirectories(Path.Combine(target, "migration-backups")).Length;
        UserDataService.Migrate(target, new[] { old });
        Check(File.ReadAllText(Path.Combine(target, "presets.json")) == "new user data", "no overwrite on reinstall");
        Check(Directory.GetDirectories(Path.Combine(target, "migration-backups")).Length == count, "idempotent");
        string conflict = Path.Combine(root, "conflict"); Directory.CreateDirectory(conflict);
        File.WriteAllText(Path.Combine(conflict, "presets.json"), "existing destination");
        UserDataService.Migrate(conflict, new[] { old });
        Check(File.ReadAllText(Path.Combine(conflict, "presets.json")) == "existing destination", "conflict preserved");
        string failed = Path.Combine(root, "failed");
        using (File.Open(Path.Combine(old, "presets.cfg"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool rejected = false;
            try { UserDataService.Migrate(failed, new[] { old }); } catch (IOException) { rejected = true; }
            Check(rejected, "locked source fails closed");
            Check(!File.Exists(Path.Combine(failed, ".migration-complete")), "no false completion");
        }
        UserDataService.Migrate(failed, new[] { old });
        Check(File.Exists(Path.Combine(failed, ".migration-complete")), "retry completes");
        string clean = Path.Combine(root, "clean");
        UserDataService.Migrate(clean, new[] { Path.Combine(root, "missing") });
        Check(!File.Exists(Path.Combine(clean, "presets.json")), "no invented presets");
        bool missingHashRejected = false;
        try { new UpdateDownloader().DownloadAsync(new UpdateInfo { DownloadUrl = "https://example.invalid/update.exe" }, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult(); }
        catch (UpdateException) { missingHashRejected = true; }
        Check(missingHashRejected, "unverified download rejected before network access");
        string digest = new string('a', 64);
        string sums = new string('b', 64) + "  Velqora.exe\n" + digest + "  Velqora-Setup-Windows-v4.0.0.exe\n";
        Check(UpdateService.ParseChecksum(sums, "Velqora-Setup-Windows-v4.0.0.exe") == digest, "installer checksum matched by exact filename");
        bool wrongPackageRejected = false;
        try { UpdateService.ParseChecksum(sums, "Velqora-Setup-Windows-v4.1.0.exe"); } catch (UpdateException) { wrongPackageRejected = true; }
        Check(wrongPackageRejected, "wrong installer checksum rejected");
        string installed = Path.Combine(root, "installed"); Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "unins000.exe"), "synthetic");
        Check(new UpdateService("3.2.0", Path.Combine(installed, "Velqora.exe"), target).UsesInstaller, "installed channel selects setup package");
        Console.WriteLine("PASS: " + assertions + " migration assertions. Synthetic fixtures: " + root);
    }
}
