using Microsoft.Data.Sqlite;
using Picazhu.Core;

namespace Picazhu.Data;

public sealed class CatalogRepository(IAppPaths appPaths) : ICatalogRepository
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(appPaths.DatabasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              PRAGMA journal_mode=WAL;
                              PRAGMA synchronous=NORMAL;
                              CREATE TABLE IF NOT EXISTS watched_roots (
                                  id TEXT PRIMARY KEY,
                                  path TEXT NOT NULL UNIQUE,
                                  display_name TEXT NOT NULL,
                                  include_subfolders INTEGER NOT NULL,
                                  is_enabled INTEGER NOT NULL,
                                  last_scan_started_utc TEXT NULL,
                                  last_scan_completed_utc TEXT NULL,
                                  created_utc TEXT NOT NULL,
                                  updated_utc TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS folders (
                                  id TEXT PRIMARY KEY,
                                  root_id TEXT NOT NULL,
                                  parent_folder_id TEXT NULL,
                                  full_path TEXT NOT NULL UNIQUE,
                                  name TEXT NOT NULL,
                                  relative_path TEXT NOT NULL,
                                  item_count INTEGER NOT NULL DEFAULT 0,
                                  subfolder_count INTEGER NOT NULL DEFAULT 0,
                                  last_seen_utc TEXT NOT NULL,
                                  updated_utc TEXT NOT NULL
                              );
                              CREATE INDEX IF NOT EXISTS idx_folders_root_id ON folders(root_id);
                              CREATE INDEX IF NOT EXISTS idx_folders_parent_folder_id ON folders(parent_folder_id);
                              CREATE TABLE IF NOT EXISTS media_items (
                                  id TEXT PRIMARY KEY,
                                  folder_id TEXT NOT NULL,
                                  full_path TEXT NOT NULL UNIQUE,
                                  file_name TEXT NOT NULL,
                                  extension TEXT NOT NULL,
                                  media_kind TEXT NOT NULL,
                                  mime_type TEXT NULL,
                                  size_bytes INTEGER NOT NULL,
                                  created_utc TEXT NULL,
                                  modified_utc TEXT NULL,
                                  last_seen_utc TEXT NOT NULL,
                                  width INTEGER NULL,
                                  height INTEGER NULL,
                                  duration_ms INTEGER NULL,
                                  orientation INTEGER NULL,
                                  is_hidden INTEGER NOT NULL DEFAULT 0,
                                  is_supported INTEGER NOT NULL DEFAULT 1,
                                  metadata_state TEXT NOT NULL,
                                  thumb_state TEXT NOT NULL,
                                  file_signature TEXT NULL,
                                  quick_hash TEXT NULL,
                                  thumbnail_relative_path TEXT NULL,
                                  thumbnail_cache_key TEXT NULL,
                                  created_row_utc TEXT NOT NULL,
                                  updated_row_utc TEXT NOT NULL
                              );
                              CREATE INDEX IF NOT EXISTS idx_media_items_folder_id ON media_items(folder_id);
                              CREATE INDEX IF NOT EXISTS idx_media_items_media_kind ON media_items(media_kind);
                              CREATE INDEX IF NOT EXISTS idx_media_items_modified_utc ON media_items(modified_utc);
                              CREATE TABLE IF NOT EXISTS media_metadata (
                                  media_item_id TEXT PRIMARY KEY,
                                  camera_make TEXT NULL,
                                  camera_model TEXT NULL,
                                  date_taken_utc TEXT NULL,
                                  gps_lat REAL NULL,
                                  gps_lon REAL NULL,
                                  codec TEXT NULL,
                                  bitrate INTEGER NULL,
                                  frame_rate REAL NULL,
                                  color_profile TEXT NULL,
                                  notes_json TEXT NULL
                              );
                              CREATE TABLE IF NOT EXISTS thumbnails (
                                  media_item_id TEXT PRIMARY KEY,
                                  cache_key TEXT NOT NULL UNIQUE,
                                  relative_cache_path TEXT NOT NULL,
                                  width INTEGER NOT NULL,
                                  height INTEGER NOT NULL,
                                  format TEXT NOT NULL,
                                  bytes_size INTEGER NOT NULL,
                                  generated_utc TEXT NOT NULL,
                                  last_accessed_utc TEXT NOT NULL
                              );
                              CREATE TABLE IF NOT EXISTS ai_analysis (
                                  media_item_id TEXT PRIMARY KEY,
                                  analysis_state TEXT NOT NULL,
                                  provider_id TEXT NULL,
                                  provider_model TEXT NULL,
                                  caption TEXT NULL,
                                  tags_json TEXT NULL,
                                  objects_json TEXT NULL,
                                  ocr_text TEXT NULL,
                                  combined_text TEXT NULL,
                                  error_text TEXT NULL,
                                  queued_utc TEXT NULL,
                                  started_utc TEXT NULL,
                                  completed_utc TEXT NULL,
                                  updated_utc TEXT NOT NULL
                              );
                              CREATE INDEX IF NOT EXISTS idx_ai_analysis_state ON ai_analysis(analysis_state);
                              CREATE TABLE IF NOT EXISTS app_settings (
                                  key TEXT PRIMARY KEY,
                                  value_json TEXT NOT NULL
                              );
                              CREATE VIRTUAL TABLE IF NOT EXISTS media_search USING fts5(
                                  media_item_id UNINDEXED,
                                  file_name,
                                  folder_name,
                                  relative_path,
                                  extension,
                                  media_kind,
                                  camera_make,
                                  camera_model,
                                  codec
                              );
                              """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WatchedRoot>> GetWatchedRootsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, path, display_name, include_subfolders, is_enabled,
                                     created_utc, updated_utc, last_scan_started_utc, last_scan_completed_utc
                              FROM watched_roots
                              ORDER BY display_name;
                              """;
        return await ReadListAsync(command, reader => new WatchedRoot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) == 1,
            reader.GetInt32(4) == 1,
            DateTimeOffset.Parse(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6)),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8))), cancellationToken);
    }

    public async Task<WatchedRoot> AddWatchedRootAsync(string path, bool includeSubfolders, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var now = DateTimeOffset.UtcNow;
        var root = new WatchedRoot(Guid.NewGuid().ToString("N"), fullPath, Path.GetFileName(fullPath), includeSubfolders, true, now, now, null, null);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO watched_roots (id, path, display_name, include_subfolders, is_enabled, created_utc, updated_utc)
                                  VALUES ($id, $path, $display, $includeSubfolders, 1, $created, $updated)
                                  ON CONFLICT(path) DO UPDATE SET
                                      include_subfolders = excluded.include_subfolders,
                                      updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$id", root.Id);
            command.Parameters.AddWithValue("$path", root.Path);
            command.Parameters.AddWithValue("$display", root.DisplayName);
            command.Parameters.AddWithValue("$includeSubfolders", root.IncludeSubfolders ? 1 : 0);
            command.Parameters.AddWithValue("$created", root.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updated", root.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }

        return (await GetWatchedRootsAsync(cancellationToken)).First(item => string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RemoveWatchedRootAsync(string id, CancellationToken cancellationToken = default)
    {
        var roots = await GetWatchedRootsAsync(cancellationToken);
        var root = roots.FirstOrDefault(item => item.Id == id);
        if (root is null)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var normalizedRootPath = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var mediaPrefix = $"{normalizedRootPath}{Path.DirectorySeparatorChar}%";
            var exactRootPath = normalizedRootPath;

            await ExecuteAsync(connection,
                """
                DELETE FROM media_search
                WHERE media_item_id IN (
                    SELECT id FROM media_items
                    WHERE full_path = $exactRootPath OR full_path LIKE $mediaPrefix
                );
                """,
                [new("$exactRootPath", exactRootPath), new("$mediaPrefix", mediaPrefix)],
                cancellationToken);

            await ExecuteAsync(connection,
                """
                DELETE FROM media_metadata
                WHERE media_item_id IN (
                    SELECT id FROM media_items
                    WHERE full_path = $exactRootPath OR full_path LIKE $mediaPrefix
                );
                """,
                [new("$exactRootPath", exactRootPath), new("$mediaPrefix", mediaPrefix)],
                cancellationToken);

            await ExecuteAsync(connection,
                """
                DELETE FROM thumbnails
                WHERE media_item_id IN (
                    SELECT id FROM media_items
                    WHERE full_path = $exactRootPath OR full_path LIKE $mediaPrefix
                );
                """,
                [new("$exactRootPath", exactRootPath), new("$mediaPrefix", mediaPrefix)],
                cancellationToken);

            await ExecuteAsync(connection,
                """
                DELETE FROM ai_analysis
                WHERE media_item_id IN (
                    SELECT id FROM media_items
                    WHERE full_path = $exactRootPath OR full_path LIKE $mediaPrefix
                );
                """,
                [new("$exactRootPath", exactRootPath), new("$mediaPrefix", mediaPrefix)],
                cancellationToken);

            await ExecuteAsync(connection,
                """
                DELETE FROM media_items
                WHERE full_path = $exactRootPath OR full_path LIKE $mediaPrefix;
                """,
                [new("$exactRootPath", exactRootPath), new("$mediaPrefix", mediaPrefix)],
                cancellationToken);
            await ExecuteAsync(connection, "DELETE FROM folders WHERE root_id = $id;", [new("$id", id)], cancellationToken);
            await ExecuteAsync(connection, "DELETE FROM watched_roots WHERE id = $id;", [new("$id", id)], cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<FolderEntry>> GetFoldersAsync(string rootId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, root_id, parent_folder_id, full_path, name, relative_path, item_count, subfolder_count, last_seen_utc, updated_utc
                              FROM folders WHERE root_id = $rootId ORDER BY full_path;
                              """;
        command.Parameters.AddWithValue("$rootId", rootId);
        return await ReadListAsync(command, reader => new FolderEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            DateTimeOffset.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9))), cancellationToken);
    }

    public async Task<IReadOnlyList<MediaItem>> QueryMediaAsync(MediaQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = """
                  SELECT m.id, m.folder_id, m.full_path, m.file_name, m.extension, m.media_kind, m.mime_type, m.size_bytes,
                         m.created_utc, m.modified_utc, m.last_seen_utc, m.width, m.height, m.duration_ms, m.orientation,
                         m.is_hidden, m.is_supported, m.metadata_state, m.thumb_state, m.file_signature, m.quick_hash,
                         m.thumbnail_relative_path, m.thumbnail_cache_key, m.created_row_utc, m.updated_row_utc
                  FROM media_items m
                  INNER JOIN folders f ON f.id = m.folder_id
                  LEFT JOIN media_metadata mm ON mm.media_item_id = m.id
                  LEFT JOIN ai_analysis ai ON ai.media_item_id = m.id
                  WHERE 1 = 1
                  """;

        if (!string.IsNullOrWhiteSpace(query.FolderPath))
        {
            var normalizedFolderPath = Path.GetFullPath(query.FolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (query.IncludeSubfolders)
            {
                sql += " AND (m.full_path = $folderExact OR m.full_path LIKE $folderPrefix) ";
                command.Parameters.AddWithValue("$folderExact", normalizedFolderPath);
                command.Parameters.AddWithValue("$folderPrefix", $"{normalizedFolderPath}{Path.DirectorySeparatorChar}%");
            }
            else
            {
                sql += " AND f.full_path = $folderExact ";
                command.Parameters.AddWithValue("$folderExact", normalizedFolderPath);
            }
        }

        if (query.ImagesOnly)
        {
            sql += " AND m.media_kind = 'Image' ";
        }

        if (query.VideosOnly)
        {
            sql += " AND m.media_kind = 'Video' ";
        }

        if (query.LargeFilesOnly)
        {
            sql += " AND m.size_bytes >= 209715200 ";
        }

        if (query.RecentOnly)
        {
            sql += " AND COALESCE(m.modified_utc, m.created_utc, m.last_seen_utc) >= $recentCutoff ";
            command.Parameters.AddWithValue("$recentCutoff", DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));
        }

        if (query.PortraitOnly)
        {
            sql += " AND COALESCE(m.height, 0) > COALESCE(m.width, 0) ";
        }

        if (query.LandscapeOnly)
        {
            sql += " AND COALESCE(m.width, 0) >= COALESCE(m.height, 0) ";
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var parsed = SearchQueryParser.Parse(query.SearchText);
            var terms = parsed.IncludeTerms.Concat(parsed.ExactPhrase is null ? [] : [parsed.ExactPhrase]).ToArray();
            for (var i = 0; i < terms.Length; i++)
            {
                var name = $"$term{i}";
                sql += $" AND (m.file_name LIKE {name} OR m.full_path LIKE {name} OR f.name LIKE {name} OR f.relative_path LIKE {name} OR COALESCE(mm.camera_make,'') LIKE {name} OR COALESCE(mm.camera_model,'') LIKE {name} OR COALESCE(ai.combined_text,'') LIKE {name} OR COALESCE(ai.ocr_text,'') LIKE {name} OR COALESCE(ai.caption,'') LIKE {name}) ";
                command.Parameters.AddWithValue(name, $"%{terms[i]}%");
            }

            for (var i = 0; i < parsed.ExcludeTerms.Count; i++)
            {
                var name = $"$exclude{i}";
                sql += $" AND m.full_path NOT LIKE {name} ";
                command.Parameters.AddWithValue(name, $"%{parsed.ExcludeTerms[i]}%");
            }

            if (!string.IsNullOrWhiteSpace(parsed.FolderTerm))
            {
                sql += " AND f.full_path LIKE $folderTerm ";
                command.Parameters.AddWithValue("$folderTerm", $"%{parsed.FolderTerm}%");
            }
        }

        sql += query.SortMode switch
        {
            SortMode.ModifiedDate => " ORDER BY COALESCE(m.modified_utc, m.created_utc, m.last_seen_utc) DESC ",
            SortMode.CreatedDate => " ORDER BY COALESCE(m.created_utc, m.modified_utc, m.last_seen_utc) DESC ",
            SortMode.Size => " ORDER BY m.size_bytes DESC ",
            SortMode.Duration => " ORDER BY COALESCE(m.duration_ms, 0) DESC ",
            SortMode.Type => " ORDER BY m.extension ASC, m.file_name ASC ",
            _ => " ORDER BY m.file_name COLLATE NOCASE ASC "
        };
        if (query.Limit > 0)
        {
            sql += " LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", query.Limit);
        }
        command.CommandText = sql;
        return await ReadListAsync(command, MapMediaItem, cancellationToken);
    }

    public async Task<MediaItem?> GetMediaItemAsync(string mediaItemId, CancellationToken cancellationToken = default)
    {
        var items = await QueryMediaAsync(new MediaQuery(null, true, null, false, false, false, false, false, false, SortMode.Name, 0), cancellationToken);
        return items.FirstOrDefault(item => item.Id == mediaItemId);
    }

    public async Task<MediaMetadata?> GetMediaMetadataAsync(string mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT media_item_id, camera_make, camera_model, date_taken_utc, gps_lat, gps_lon, codec, bitrate, frame_rate, color_profile, notes_json
                              FROM media_metadata WHERE media_item_id = $id;
                              """;
        command.Parameters.AddWithValue("$id", mediaItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new MediaMetadata(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetDouble(4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    public async Task UpsertFolderAsync(FolderEntry folder, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO folders (id, root_id, parent_folder_id, full_path, name, relative_path, item_count, subfolder_count, last_seen_utc, updated_utc)
                                  VALUES ($id, $rootId, $parentFolderId, $fullPath, $name, $relativePath, $itemCount, $subfolderCount, $lastSeenUtc, $updatedUtc)
                                  ON CONFLICT(full_path) DO UPDATE SET
                                      root_id = excluded.root_id,
                                      parent_folder_id = excluded.parent_folder_id,
                                      name = excluded.name,
                                      relative_path = excluded.relative_path,
                                      item_count = excluded.item_count,
                                      subfolder_count = excluded.subfolder_count,
                                      last_seen_utc = excluded.last_seen_utc,
                                      updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$id", folder.Id);
            command.Parameters.AddWithValue("$rootId", folder.RootId);
            command.Parameters.AddWithValue("$parentFolderId", (object?)folder.ParentFolderId ?? DBNull.Value);
            command.Parameters.AddWithValue("$fullPath", folder.FullPath);
            command.Parameters.AddWithValue("$name", folder.Name);
            command.Parameters.AddWithValue("$relativePath", folder.RelativePath);
            command.Parameters.AddWithValue("$itemCount", folder.ItemCount);
            command.Parameters.AddWithValue("$subfolderCount", folder.SubfolderCount);
            command.Parameters.AddWithValue("$lastSeenUtc", folder.LastSeenUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", folder.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpsertMediaItemAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO media_items (id, folder_id, full_path, file_name, extension, media_kind, mime_type, size_bytes,
                                      created_utc, modified_utc, last_seen_utc, width, height, duration_ms, orientation, is_hidden, is_supported,
                                      metadata_state, thumb_state, file_signature, quick_hash, thumbnail_relative_path, thumbnail_cache_key, created_row_utc, updated_row_utc)
                                  VALUES ($id, $folderId, $fullPath, $fileName, $extension, $mediaKind, $mimeType, $sizeBytes,
                                      $createdUtc, $modifiedUtc, $lastSeenUtc, $width, $height, $durationMs, $orientation, $isHidden, $isSupported,
                                      $metadataState, $thumbState, $fileSignature, $quickHash, $thumbRelPath, $thumbCacheKey, $createdRowUtc, $updatedRowUtc)
                                  ON CONFLICT(full_path) DO UPDATE SET
                                      folder_id = excluded.folder_id,
                                      file_name = excluded.file_name,
                                      extension = excluded.extension,
                                      media_kind = excluded.media_kind,
                                      mime_type = excluded.mime_type,
                                      size_bytes = excluded.size_bytes,
                                      created_utc = excluded.created_utc,
                                      modified_utc = excluded.modified_utc,
                                      last_seen_utc = excluded.last_seen_utc,
                                      width = COALESCE(media_items.width, excluded.width),
                                      height = COALESCE(media_items.height, excluded.height),
                                      duration_ms = COALESCE(media_items.duration_ms, excluded.duration_ms),
                                      orientation = COALESCE(media_items.orientation, excluded.orientation),
                                      is_hidden = excluded.is_hidden,
                                      is_supported = excluded.is_supported,
                                      metadata_state = excluded.metadata_state,
                                      thumb_state = excluded.thumb_state,
                                      file_signature = excluded.file_signature,
                                      quick_hash = excluded.quick_hash,
                                      updated_row_utc = excluded.updated_row_utc;
                                  """;
            AddMediaParameters(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await UpsertSearchAsync(connection, item, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpsertMediaMetadataAsync(MediaMetadata metadata, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO media_metadata (media_item_id, camera_make, camera_model, date_taken_utc, gps_lat, gps_lon, codec, bitrate, frame_rate, color_profile, notes_json)
                                  VALUES ($id, $make, $model, $dateTaken, $gpsLat, $gpsLon, $codec, $bitrate, $frameRate, $colorProfile, $notes)
                                  ON CONFLICT(media_item_id) DO UPDATE SET
                                      camera_make = excluded.camera_make,
                                      camera_model = excluded.camera_model,
                                      date_taken_utc = excluded.date_taken_utc,
                                      gps_lat = excluded.gps_lat,
                                      gps_lon = excluded.gps_lon,
                                      codec = excluded.codec,
                                      bitrate = excluded.bitrate,
                                      frame_rate = excluded.frame_rate,
                                      color_profile = excluded.color_profile,
                                      notes_json = excluded.notes_json;
                                  """;
            command.Parameters.AddWithValue("$id", metadata.MediaItemId);
            command.Parameters.AddWithValue("$make", (object?)metadata.CameraMake ?? DBNull.Value);
            command.Parameters.AddWithValue("$model", (object?)metadata.CameraModel ?? DBNull.Value);
            command.Parameters.AddWithValue("$dateTaken", metadata.DateTakenUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$gpsLat", metadata.GpsLat ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$gpsLon", metadata.GpsLon ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$codec", (object?)metadata.Codec ?? DBNull.Value);
            command.Parameters.AddWithValue("$bitrate", metadata.Bitrate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$frameRate", metadata.FrameRate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$colorProfile", (object?)metadata.ColorProfile ?? DBNull.Value);
            command.Parameters.AddWithValue("$notes", (object?)metadata.NotesJson ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpsertThumbnailAsync(ThumbnailRecord thumbnail, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO thumbnails (media_item_id, cache_key, relative_cache_path, width, height, format, bytes_size, generated_utc, last_accessed_utc)
                                  VALUES ($id, $cacheKey, $relativePath, $width, $height, $format, $bytesSize, $generatedUtc, $lastAccessedUtc)
                                  ON CONFLICT(media_item_id) DO UPDATE SET
                                      cache_key = excluded.cache_key,
                                      relative_cache_path = excluded.relative_cache_path,
                                      width = excluded.width,
                                      height = excluded.height,
                                      format = excluded.format,
                                      bytes_size = excluded.bytes_size,
                                      generated_utc = excluded.generated_utc,
                                      last_accessed_utc = excluded.last_accessed_utc;
                                  """;
            command.Parameters.AddWithValue("$id", thumbnail.MediaItemId);
            command.Parameters.AddWithValue("$cacheKey", thumbnail.CacheKey);
            command.Parameters.AddWithValue("$relativePath", thumbnail.RelativeCachePath);
            command.Parameters.AddWithValue("$width", thumbnail.Width);
            command.Parameters.AddWithValue("$height", thumbnail.Height);
            command.Parameters.AddWithValue("$format", thumbnail.Format);
            command.Parameters.AddWithValue("$bytesSize", thumbnail.BytesSize);
            command.Parameters.AddWithValue("$generatedUtc", thumbnail.GeneratedUtc.ToString("O"));
            command.Parameters.AddWithValue("$lastAccessedUtc", thumbnail.LastAccessedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task EnsureAiAnalysisRecordAsync(string mediaItemId, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO ai_analysis (media_item_id, analysis_state, queued_utc, updated_utc)
                                  VALUES ($id, $state, $queuedUtc, $updatedUtc)
                                  ON CONFLICT(media_item_id) DO NOTHING;
                                  """;
            command.Parameters.AddWithValue("$id", mediaItemId);
            command.Parameters.AddWithValue("$state", AiAnalysisState.Pending.ToString());
            command.Parameters.AddWithValue("$queuedUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AiAnalysisRecord?> GetAiAnalysisAsync(string mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT media_item_id, analysis_state, provider_id, provider_model, caption, tags_json, objects_json, ocr_text,
                                     combined_text, error_text, queued_utc, started_utc, completed_utc, updated_utc
                              FROM ai_analysis
                              WHERE media_item_id = $id;
                              """;
        command.Parameters.AddWithValue("$id", mediaItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AiAnalysisRecord(
            reader.GetString(0),
            Enum.Parse<AiAnalysisState>(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
            DateTimeOffset.Parse(reader.GetString(13)));
    }

    public async Task<IReadOnlyDictionary<string, AiAnalysisState>> GetAiAnalysisStatesAsync(IReadOnlyList<string> mediaItemIds, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, AiAnalysisState>(StringComparer.OrdinalIgnoreCase);
        if (mediaItemIds.Count == 0)
        {
            return results;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in mediaItemIds.Chunk(500))
        {
            await using var command = connection.CreateCommand();
            var parameterNames = new List<string>(batch.Length);
            for (var index = 0; index < batch.Length; index++)
            {
                var parameterName = $"$id{index}";
                parameterNames.Add(parameterName);
                command.Parameters.AddWithValue(parameterName, batch[index]);
            }

            command.CommandText = $"""
                                   SELECT media_item_id, analysis_state
                                   FROM ai_analysis
                                   WHERE media_item_id IN ({string.Join(", ", parameterNames)});
                                   """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results[reader.GetString(0)] = Enum.Parse<AiAnalysisState>(reader.GetString(1));
            }
        }

        return results;
    }

    public async Task UpdateAiAnalysisAsync(AiAnalysisRecord record, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO ai_analysis (
                                      media_item_id, analysis_state, provider_id, provider_model, caption, tags_json, objects_json, ocr_text,
                                      combined_text, error_text, queued_utc, started_utc, completed_utc, updated_utc)
                                  VALUES (
                                      $id, $state, $providerId, $providerModel, $caption, $tagsJson, $objectsJson, $ocrText,
                                      $combinedText, $errorText, $queuedUtc, $startedUtc, $completedUtc, $updatedUtc)
                                  ON CONFLICT(media_item_id) DO UPDATE SET
                                      analysis_state = excluded.analysis_state,
                                      provider_id = excluded.provider_id,
                                      provider_model = excluded.provider_model,
                                      caption = excluded.caption,
                                      tags_json = excluded.tags_json,
                                      objects_json = excluded.objects_json,
                                      ocr_text = excluded.ocr_text,
                                      combined_text = excluded.combined_text,
                                      error_text = excluded.error_text,
                                      queued_utc = excluded.queued_utc,
                                      started_utc = excluded.started_utc,
                                      completed_utc = excluded.completed_utc,
                                      updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$id", record.MediaItemId);
            command.Parameters.AddWithValue("$state", record.AnalysisState.ToString());
            command.Parameters.AddWithValue("$providerId", (object?)record.ProviderId ?? DBNull.Value);
            command.Parameters.AddWithValue("$providerModel", (object?)record.ProviderModel ?? DBNull.Value);
            command.Parameters.AddWithValue("$caption", (object?)record.Caption ?? DBNull.Value);
            command.Parameters.AddWithValue("$tagsJson", (object?)record.TagsJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$objectsJson", (object?)record.ObjectsJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$ocrText", (object?)record.OcrText ?? DBNull.Value);
            command.Parameters.AddWithValue("$combinedText", (object?)record.CombinedText ?? DBNull.Value);
            command.Parameters.AddWithValue("$errorText", (object?)record.ErrorText ?? DBNull.Value);
            command.Parameters.AddWithValue("$queuedUtc", record.QueuedUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$startedUtc", record.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$completedUtc", record.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$updatedUtc", record.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<(int Total, int Pending, int Completed, int Failed)> GetAiAnalysisCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var total = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM media_items WHERE media_kind IN ('Image', 'Video');", cancellationToken);
        var pending = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM ai_analysis WHERE analysis_state IN ('Pending', 'Processing');", cancellationToken);
        var completed = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM ai_analysis WHERE analysis_state = 'Done';", cancellationToken);
        var failed = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM ai_analysis WHERE analysis_state = 'Failed';", cancellationToken);
        return (total, pending, completed, failed);
    }

    public async Task UpdateMediaProbeAsync(string mediaItemId, ProbeResult result, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection,
                """
                UPDATE media_items
                SET width = $width,
                    height = $height,
                    duration_ms = $durationMs,
                    orientation = $orientation,
                    is_supported = $isSupported,
                    metadata_state = $metadataState,
                    updated_row_utc = $updatedUtc
                WHERE id = $id;
                """,
                [
                    new("$width", result.Width ?? (object)DBNull.Value),
                    new("$height", result.Height ?? (object)DBNull.Value),
                    new("$durationMs", result.DurationMs ?? (object)DBNull.Value),
                    new("$orientation", result.Orientation ?? (object)DBNull.Value),
                    new("$isSupported", result.IsSupported ? 1 : 0),
                    new("$metadataState", MetadataState.Done.ToString()),
                    new("$updatedUtc", DateTimeOffset.UtcNow.ToString("O")),
                    new("$id", mediaItemId)
                ],
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }

        await UpsertMediaMetadataAsync(result.Metadata, cancellationToken);
    }

    public async Task UpdateMediaThumbnailStateAsync(string mediaItemId, ThumbState thumbState, string? cacheKey, string? relativePath, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection,
                """
                UPDATE media_items
                SET thumb_state = $state,
                    thumbnail_cache_key = $cacheKey,
                    thumbnail_relative_path = $relativePath,
                    updated_row_utc = $updatedUtc
                WHERE id = $id;
                """,
                [
                    new("$state", thumbState.ToString()),
                    new("$cacheKey", (object?)cacheKey ?? DBNull.Value),
                    new("$relativePath", (object?)relativePath ?? DBNull.Value),
                    new("$updatedUtc", DateTimeOffset.UtcNow.ToString("O")),
                    new("$id", mediaItemId)
                ],
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task PruneMissingAsync(string rootId, IReadOnlySet<string> seenFolders, IReadOnlySet<string> seenMedia, CancellationToken cancellationToken = default)
    {
        var roots = await GetWatchedRootsAsync(cancellationToken);
        var root = roots.FirstOrDefault(item => item.Id == rootId);
        if (root is null)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            foreach (var folder in (await GetFoldersAsync(rootId, cancellationToken)).Where(item => !seenFolders.Contains(item.FullPath)))
            {
                await ExecuteAsync(connection, "DELETE FROM folders WHERE id = $id;", [new("$id", folder.Id)], cancellationToken);
            }

            foreach (var media in (await QueryMediaAsync(new MediaQuery(root.Path, true, null, false, false, false, false, false, false, SortMode.Name, 0), cancellationToken)).Where(item => !seenMedia.Contains(item.FullPath)))
            {
                await ExecuteAsync(connection, "DELETE FROM media_search WHERE media_item_id = $id;", [new("$id", media.Id)], cancellationToken);
                await ExecuteAsync(connection, "DELETE FROM media_metadata WHERE media_item_id = $id;", [new("$id", media.Id)], cancellationToken);
                await ExecuteAsync(connection, "DELETE FROM thumbnails WHERE media_item_id = $id;", [new("$id", media.Id)], cancellationToken);
                await ExecuteAsync(connection, "DELETE FROM ai_analysis WHERE media_item_id = $id;", [new("$id", media.Id)], cancellationToken);
                await ExecuteAsync(connection, "DELETE FROM media_items WHERE id = $id;", [new("$id", media.Id)], cancellationToken);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<DiagnosticsSnapshot> GetDiagnosticsAsync(int watcherCount, int scanQueueDepth, int metadataQueueDepth, int thumbQueueDepth, long thumbCacheBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var roots = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM watched_roots;", cancellationToken);
        var folders = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM folders;", cancellationToken);
        var media = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM media_items;", cancellationToken);
        var failedMetadata = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM media_items WHERE metadata_state = 'Failed';", cancellationToken);
        var failedThumbs = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM media_items WHERE thumb_state = 'Failed';", cancellationToken);
        var pendingMetadata = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM media_items WHERE metadata_state = 'Pending';", cancellationToken);
        var pendingThumbs = await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM media_items WHERE thumb_state = 'Pending';", cancellationToken);
        var lastScan = await ScalarAsync<string?>(connection, "SELECT MAX(last_scan_completed_utc) FROM watched_roots;", cancellationToken);

        return new DiagnosticsSnapshot(
            roots,
            folders,
            media,
            failedMetadata,
            failedThumbs,
            pendingMetadata,
            pendingThumbs,
            thumbCacheBytes,
            watcherCount,
            scanQueueDepth,
            metadataQueueDepth,
            thumbQueueDepth,
            string.IsNullOrWhiteSpace(lastScan) ? null : DateTimeOffset.Parse(lastScan));
    }

    public Task MarkRootScanStartedAsync(string rootId, CancellationToken cancellationToken = default)
        => UpdateRootScanAsync(rootId, "last_scan_started_utc", cancellationToken);

    public Task MarkRootScanCompletedAsync(string rootId, CancellationToken cancellationToken = default)
        => UpdateRootScanAsync(rootId, "last_scan_completed_utc", cancellationToken);

    public async Task RebuildCatalogAsync(CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(appPaths.DatabasePath))
            {
                File.Delete(appPaths.DatabasePath);
            }
        }
        finally
        {
            _writeGate.Release();
        }

        await InitializeAsync(cancellationToken);
    }

    private async Task UpdateRootScanAsync(string rootId, string column, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, $"UPDATE watched_roots SET {column} = $value, updated_utc = $value WHERE id = $id;",
                [new("$value", DateTimeOffset.UtcNow.ToString("O")), new("$id", rootId)], cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task UpsertSearchAsync(SqliteConnection connection, MediaItem item, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "DELETE FROM media_search WHERE media_item_id = $id;", [new("$id", item.Id)], cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO media_search (media_item_id, file_name, folder_name, relative_path, extension, media_kind, camera_make, camera_model, codec)
                              SELECT m.id, m.file_name, f.name, f.relative_path, m.extension, m.media_kind,
                                     COALESCE(mm.camera_make, ''), COALESCE(mm.camera_model, ''), COALESCE(mm.codec, '')
                              FROM media_items m
                              INNER JOIN folders f ON f.id = m.folder_id
                              LEFT JOIN media_metadata mm ON mm.media_item_id = m.id
                              WHERE m.id = $id;
                              """;
        command.Parameters.AddWithValue("$id", item.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() => new($"Data Source={appPaths.DatabasePath};Cache=Shared");

    private static void AddMediaParameters(SqliteCommand command, MediaItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$folderId", item.FolderId);
        command.Parameters.AddWithValue("$fullPath", item.FullPath);
        command.Parameters.AddWithValue("$fileName", item.FileName);
        command.Parameters.AddWithValue("$extension", item.Extension);
        command.Parameters.AddWithValue("$mediaKind", item.MediaKind.ToString());
        command.Parameters.AddWithValue("$mimeType", (object?)item.MimeType ?? DBNull.Value);
        command.Parameters.AddWithValue("$sizeBytes", item.SizeBytes);
        command.Parameters.AddWithValue("$createdUtc", item.CreatedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$modifiedUtc", item.ModifiedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastSeenUtc", item.LastSeenUtc.ToString("O"));
        command.Parameters.AddWithValue("$width", item.Width ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$height", item.Height ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$durationMs", item.DurationMs ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$orientation", item.Orientation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$isHidden", item.IsHidden ? 1 : 0);
        command.Parameters.AddWithValue("$isSupported", item.IsSupported ? 1 : 0);
        command.Parameters.AddWithValue("$metadataState", item.MetadataState.ToString());
        command.Parameters.AddWithValue("$thumbState", item.ThumbState.ToString());
        command.Parameters.AddWithValue("$fileSignature", (object?)item.FileSignature ?? DBNull.Value);
        command.Parameters.AddWithValue("$quickHash", (object?)item.QuickHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$thumbRelPath", (object?)item.ThumbnailRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$thumbCacheKey", (object?)item.ThumbnailCacheKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdRowUtc", item.CreatedRowUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedRowUtc", item.UpdatedRowUtc.ToString("O"));
    }

    private static MediaItem MapMediaItem(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        Enum.Parse<MediaKind>(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetInt64(7),
        reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
        DateTimeOffset.Parse(reader.GetString(10)),
        reader.IsDBNull(11) ? null : reader.GetInt32(11),
        reader.IsDBNull(12) ? null : reader.GetInt32(12),
        reader.IsDBNull(13) ? null : reader.GetInt64(13),
        reader.IsDBNull(14) ? null : reader.GetInt32(14),
        reader.GetInt32(15) == 1,
        reader.GetInt32(16) == 1,
        Enum.Parse<MetadataState>(reader.GetString(17)),
        Enum.Parse<ThumbState>(reader.GetString(18)),
        reader.IsDBNull(19) ? null : reader.GetString(19),
        reader.IsDBNull(20) ? null : reader.GetString(20),
        reader.IsDBNull(21) ? null : reader.GetString(21),
        reader.IsDBNull(22) ? null : reader.GetString(22),
        DateTimeOffset.Parse(reader.GetString(23)),
        DateTimeOffset.Parse(reader.GetString(24)));

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(SqliteCommand command, Func<SqliteDataReader, T> map, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, IEnumerable<KeyValuePair<string, object>> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            return default!;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }
}
