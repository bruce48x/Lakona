using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lakona.Hub.Installation;

public sealed record PackageFile(long Size, string Sha256);
public sealed record PackageManifest(int Protocol, string Version, string Platform, Dictionary<string, PackageFile> Files);
public sealed record InstallationJournal(string Stage, string Version, string Token, bool HadPrevious);
public sealed record HealthConfirmation(string Version, string Token);

[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(InstallationJournal))]
[JsonSerializable(typeof(HealthConfirmation))]
public partial class InstallationJson : JsonSerializerContext;

public sealed class SignedPackage
{
    public const string ManifestName = ".lakona-package.json";
    public const string SignatureName = ".lakona-package.sig";
    private const long MaxPayloadSize = 2L * 1024 * 1024 * 1024;
    private readonly string publicKey;

    public SignedPackage(string publicKey) => this.publicKey = publicKey;

    public static SignedPackage Release()
    {
        using var resource = typeof(SignedPackage).Assembly.GetManifestResourceStream("Lakona.Hub.ReleaseKey.pem")
            ?? throw new InvalidOperationException("This build has no release signing key. Use the current MSI release.");
        using var reader = new StreamReader(resource);
        return new SignedPackage(reader.ReadToEnd());
    }

    public PackageManifest Extract(string archive, string destination, CancellationToken cancellationToken = default)
    {
        InstallationPaths.RejectLinks(destination);
        if (Directory.Exists(destination)) throw new IOException("The staging directory must be empty.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 10002) throw new InvalidDataException("The package has too many files.");
        var manifestBytes = ReadEntry(zip, ManifestName, 4 * 1024 * 1024);
        var signature = ReadEntry(zip, SignatureName, 1024);
        var manifest = VerifyManifest(manifestBytes, signature);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            _ = InstallationPaths.PayloadPath(destination, entry.FullName);
            if (!seen.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The package contains duplicate files or links.");
            if (entry.FullName is ManifestName or SignatureName) continue;
            if (!manifest.Files.TryGetValue(entry.FullName, out var file) || entry.Length != file.Size)
                throw new InvalidDataException("The package does not match its signed manifest.");
            total = checked(total + entry.Length);
            if (total > MaxPayloadSize) throw new InvalidDataException("The package is too large.");
        }
        if (seen.Count != manifest.Files.Count + 2) throw new InvalidDataException("The package is incomplete.");
        Directory.CreateDirectory(destination);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = InstallationPaths.PayloadPath(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyBounded(source, target, entry.Length);
        }
        VerifyDirectory(destination);
        return manifest;
    }

    public PackageManifest VerifyDirectory(string directory)
    {
        InstallationPaths.RejectLinks(directory);
        var manifest = VerifyManifest(ReadFile(directory, ManifestName, 4 * 1024 * 1024), ReadFile(directory, SignatureName, 1024));
        foreach (var (relative, file) in manifest.Files)
        {
            var path = InstallationPaths.PayloadPath(directory, relative);
            InstallationPaths.RejectLinks(path);
            using var stream = File.OpenRead(path);
            if (stream.Length != file.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An installed file failed signature-bound checksum verification.");
        }
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            InstallationPaths.RejectLinks(path);
            if (Directory.Exists(path)) continue;
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative is not ManifestName and not SignatureName && !manifest.Files.ContainsKey(relative))
                throw new InvalidDataException("The installation contains an unrecognized file.");
        }
        return manifest;
    }

    public PackageManifest ReadInstalledManifest(string directory) =>
        VerifyManifest(ReadFile(directory, ManifestName, 4 * 1024 * 1024), ReadFile(directory, SignatureName, 1024));

    public void CopyUpdater(string source, string destination)
    {
        var manifest = ReadInstalledManifest(source);
        InstallationPaths.RejectLinks(destination);
        Directory.CreateDirectory(destination);
        foreach (var (name, file) in manifest.Files.Where(pair => pair.Key == "Lakona.Hub.Updater.exe" || pair.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var original = InstallationPaths.PayloadPath(source, name);
            var copy = InstallationPaths.PayloadPath(destination, name);
            InstallationPaths.RejectLinks(original);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(original, copy, overwrite: false);
            using var stream = File.OpenRead(copy);
            if (stream.Length != file.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The updater failed checksum verification.");
        }
    }

    private PackageManifest VerifyManifest(byte[] bytes, byte[] signature)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (rsa.KeySize < 2048) throw new InvalidDataException("The release signing key is too small.");
        if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("The package signature is invalid.");
        var manifest = JsonSerializer.Deserialize(bytes, InstallationJson.Default.PackageManifest)
            ?? throw new InvalidDataException("The package manifest is empty.");
        if (manifest.Protocol != 1 || manifest.Platform != "win-x64" || !Version.TryParse(manifest.Version, out _) || manifest.Files is null ||
            !manifest.Files.ContainsKey("Lakona.Hub.exe") || !manifest.Files.ContainsKey("Lakona.Hub.Updater.exe") || manifest.Files.Count > 10000)
            throw new InvalidDataException("The package version, platform, or updater protocol is unsupported.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, file) in manifest.Files)
        {
            _ = InstallationPaths.PayloadPath(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), name);
            if (!unique.Add(name) || name is ManifestName or SignatureName || file is null || file.Size < 0 || file.Size > MaxPayloadSize ||
                file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The package file metadata is invalid.");
        }
        return manifest;
    }

    private static byte[] ReadEntry(ZipArchive zip, string name, int maximum)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException("The package has no signature or manifest.");
        if (entry.Length > maximum) throw new InvalidDataException("Package metadata is too large.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        CopyBounded(stream, buffer, entry.Length);
        return buffer.ToArray();
    }

    private static byte[] ReadFile(string root, string name, int maximum)
    {
        var path = Path.Combine(root, name);
        InstallationPaths.RejectLinks(path);
        if (new FileInfo(path).Length > maximum) throw new InvalidDataException("Package metadata is too large.");
        return File.ReadAllBytes(path);
    }

    private static void CopyBounded(Stream source, Stream destination, long expected)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = source.Read(buffer)) != 0)
        {
            total = checked(total + count);
            if (total > expected) throw new InvalidDataException("A package entry exceeds its declared size.");
            destination.Write(buffer, 0, count);
        }
        if (total != expected) throw new InvalidDataException("A package entry is incomplete.");
    }
}
