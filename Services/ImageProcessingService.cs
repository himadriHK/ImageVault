using System.Threading.Channels;
using ImageVault.Models;

namespace ImageVault.Services;

public class ImageProcessingService : IImageProcessingService
{
    private readonly IClipService _clipService;
    private readonly IVectorDbService _vectorDb;

    public event Action<ProcessingItem, int, int>? OnItemProcessed;
    public event Action<int>? OnBatchProgress;
    public event Action<string>? OnError;
    public event Action? OnBatchComplete;

    private static readonly HashSet<string> SupportedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];

    public ImageProcessingService(IClipService clipService, IVectorDbService vectorDb)
    {
        _clipService = clipService;
        _vectorDb = vectorDb;
    }

    public async Task ProcessBatchAsync(
        IReadOnlyList<ProcessingItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
        {
            OnBatchComplete?.Invoke();
            return;
        }

        var channel = Channel.CreateBounded<ProcessingItem>(
            new BoundedChannelOptions(Math.Min(items.Count, 50))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false
            });

        var consumer = ConsumeAsync(channel.Reader, items.Count, ct);
        var producer = ProduceAsync(channel.Writer, items, ct);

        await Task.WhenAll(producer, consumer);
        OnBatchComplete?.Invoke();
    }

    private async Task ProduceAsync(
        ChannelWriter<ProcessingItem> writer,
        IReadOnlyList<ProcessingItem> items,
        CancellationToken ct)
    {
        try
        {
            foreach (var item in items)
            {
                if (ct.IsCancellationRequested) break;
                await writer.WriteAsync(item, ct);
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<ProcessingItem> reader,
        int totalCount,
        CancellationToken ct)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        var processedCount = 0;
        var lockObj = new object();

        await Parallel.ForEachAsync(
            reader.ReadAllAsync(ct),
            parallelOptions,
            async (item, token) =>
            {
                try
                {
                    await ProcessSingleItemAsync(item, token);

                    int current;
                    lock (lockObj)
                    {
                        current = Interlocked.Increment(ref processedCount);
                    }

                    OnBatchProgress?.Invoke(current);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Failed to process {item.FileName}: {ex.Message}");
                }
            });
    }

    private async Task ProcessSingleItemAsync(ProcessingItem item, CancellationToken ct)
    {
        var ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return;

        if (await _vectorDb.ExistsByPathAsync(item.FilePath))
            return;

        var embedding = await _clipService.GetImageEmbeddingAsync(item.FilePath, ct);

        var fileInfo = new FileInfo(item.FilePath);

        var entity = new ImageEntity
        {
            FilePath = item.FilePath,
            FileName = item.FileName,
            FileSize = item.FileSize,
            DateAddedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DateModifiedUnixMs = item.DateModifiedUnixMs,
            Embedding = embedding
        };

        try
        {
            await _vectorDb.InsertAsync(entity);
            OnItemProcessed?.Invoke(item, 0, 0);
        }
        catch when (ExistsByPathSafe(item.FilePath))
        {
        }
    }

    private bool ExistsByPathSafe(string path)
    {
        try { return _vectorDb.ExistsByPathAsync(path).GetAwaiter().GetResult(); }
        catch { return false; }
    }

    public static List<ProcessingItem> ScanDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        return Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new ProcessingItem
                {
                    FilePath = f,
                    FileName = info.Name,
                    FileSize = info.Length,
                    DateModifiedUnixMs = ((DateTimeOffset)info.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                };
            })
            .ToList();
    }
}
