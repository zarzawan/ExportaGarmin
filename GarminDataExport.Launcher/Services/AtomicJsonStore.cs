using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GarminDataExport.Launcher.Services;

internal static class AtomicJsonStore
{
    private static readonly ConcurrentQueue<string> RecoveryNotices = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Options);
        }
        catch (JsonException originalError)
        {
            var backupPath = path + ".bak";
            if (TryReadBackup<T>(backupPath, out var recovered))
            {
                RestoreBackup(path, backupPath);
                RecoveryNotices.Enqueue(
                    $"Se restauró «{Path.GetFileName(path)}» desde su copia de seguridad. " +
                    "La copia dañada se ha conservado.");
                return recovered;
            }
            throw new InvalidDataException(
                $"El archivo «{Path.GetFileName(path)}» está dañado y no se modificará. " +
                "Conserva el archivo y su copia .bak antes de intentar repararlo.",
                originalError);
        }
        catch (IOException error)
        {
            throw new InvalidDataException(
                $"No se pudo leer «{Path.GetFileName(path)}» y no se modificará.",
                error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidDataException(
                $"Windows no permite leer «{Path.GetFileName(path)}» y no se modificará.",
                error);
        }
    }

    public static IReadOnlyList<string> ConsumeRecoveryNotices()
    {
        var result = new List<string>();
        while (RecoveryNotices.TryDequeue(out var notice))
            result.Add(notice);
        return result;
    }

    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("No se pudo determinar la carpeta del archivo.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backupPath = path + ".bak";

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    TryDelete(backupPath);
                    File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool TryReadBackup<T>(string path, out T? value)
    {
        value = default;
        if (!File.Exists(path))
            return false;
        try
        {
            value = JsonSerializer.Deserialize<T>(
                File.ReadAllText(path, Encoding.UTF8),
                Options);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RestoreBackup(string path, string backupPath)
    {
        var damagedPath = path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        File.Move(path, damagedPath);
        try
        {
            File.Copy(backupPath, path);
        }
        catch
        {
            if (!File.Exists(path) && File.Exists(damagedPath))
                File.Move(damagedPath, path);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // El archivo permanece en la carpeta privada y puede limpiarse más tarde.
        }
    }
}
