using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NJPlayer.Core.Playlist;

/// <summary>
/// Model representing a directory or playable media file on the local file system.
/// </summary>
public sealed class LocalFileSystemItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long FileSizeBytes { get; init; }
    public string FormattedSize { get; init; } = string.Empty;
    public bool IsVideo { get; init; }
    public bool IsAudio { get; init; }
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Service providing asynchronous file system browsing for local media (videos and music).
/// </summary>
public sealed class LocalMediaBrowserService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".ts", ".m4v"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".opus", ".ogg", ".aac", ".wma", ".alac", ".aiff", ".ape"
    };

    /// <summary>
    /// Returns available and ready local storage drives (e.g. C:\, D:\).
    /// </summary>
    public IReadOnlyList<(string Name, string Path)> GetDrives()
    {
        var result = new List<(string Name, string Path)>();
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (drive.IsReady)
                {
                    var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name
                        : $"{drive.Name} ({drive.VolumeLabel})";
                    result.Add((label, drive.RootDirectory.FullName));
                }
            }
        }
        catch
        {
            // Fallback to system drive if enumeration fails
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(systemDrive))
            {
                result.Add((systemDrive, systemDrive));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns quick navigation shortcuts for standard user media folders.
    /// </summary>
    public IReadOnlyList<(string Name, string Path)> GetQuickPlaces()
    {
        var list = new List<(string Name, string Path)>();

        void AddIfValid(string name, Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    list.Add((name, path));
                }
            }
            catch { }
        }

        AddIfValid("Videos", Environment.SpecialFolder.MyVideos);
        AddIfValid("Music", Environment.SpecialFolder.MyMusic);
        AddIfValid("Desktop", Environment.SpecialFolder.Desktop);

        // Downloads folder
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloads))
            {
                list.Add(("Downloads", downloads));
            }
        }
        catch { }

        return list;
    }

    /// <summary>
    /// Asynchronously enumerates subdirectories and supported media files in the given directory.
    /// </summary>
    public async Task<IReadOnlyList<LocalFileSystemItem>> EnumerateDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return Array.Empty<LocalFileSystemItem>();
        }

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var items = new List<LocalFileSystemItem>();
            var dirInfo = new DirectoryInfo(directoryPath);

            // 1. Subdirectories
            try
            {
                var subDirs = dirInfo.EnumerateDirectories()
                    .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(d => d.Name);

                foreach (var dir in subDirs)
                {
                    ct.ThrowIfCancellationRequested();
                    items.Add(new LocalFileSystemItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        FormattedSize = "Folder",
                        LastModified = dir.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            // 2. Supported media files
            try
            {
                var files = dirInfo.EnumerateFiles()
                    .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                                PlaylistManager.IsSupportedMediaFile(f.FullName))
                    .OrderBy(f => f.Name);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    var ext = file.Extension;
                    var isVideo = VideoExtensions.Contains(ext);
                    var isAudio = AudioExtensions.Contains(ext);

                    items.Add(new LocalFileSystemItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        FileSizeBytes = file.Length,
                        FormattedSize = FormatBytes(file.Length),
                        IsVideo = isVideo,
                        IsAudio = isAudio,
                        LastModified = file.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            return (IReadOnlyList<LocalFileSystemItem>)items;
        }, ct).ConfigureAwait(false);
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
