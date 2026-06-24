using System.Text.Json;
using Microsoft.Maui.Storage;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using SkiaSharp;

namespace ImageVault.Services;

public class ClipService : IClipService, IDisposable
{
    private BpeTokenizer? _tokenizer;
    private InferenceSession? _session;
    private bool _disposed;

    // Pre-allocated dummy/constant tensors (never modified, thread-safe for concurrent reads)
    private static readonly DenseTensor<long> DummyInputIds = new(new long[MaxTokens], [1, MaxTokens]);
    private static readonly DenseTensor<long> DummyAttentionMask = new(new long[MaxTokens], [1, MaxTokens]);
    private static readonly DenseTensor<float> DummyPixels = new(new float[3 * ImageSize * ImageSize], [1, 3, ImageSize, ImageSize]);

    // Thread-local pre-allocated buffers — allocated once per thread, reused across calls
    private readonly ThreadLocal<float[]> _pixelBuffer = new(() => new float[3 * ImageSize * ImageSize]);
    private readonly ThreadLocal<long[]> _textInputIds = new(() => new long[MaxTokens]);
    private readonly ThreadLocal<long[]> _textAttentionMask = new(() => new long[MaxTokens]);

    private const int ImageSize = 224;
    private const int MaxTokens = 77;
    private const int BosTokenId = 49406;
    private const int EosTokenId = 49407;
    private const float Inv255 = 1f / 255f;
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

    public bool IsModelLoaded => _session != null;

    public async Task LoadModelsAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tokenizerJson = await ReadAssetStringAsync("tokenizer.json", ct);
        progress?.Report(0.15);

        _tokenizer = await Task.Run(() => BuildTokenizer(tokenizerJson), ct);
        progress?.Report(0.30);

        var modelPath = await CacheModelFileAsync(ct);
        progress?.Report(0.70);

        _session = await CreateSessionWithGpuAsync(modelPath, ct);
        progress?.Report(1.0);
    }

    private static Task<InferenceSession> CreateSessionWithGpuAsync(string modelPath, CancellationToken ct)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = true,
            //EnableMemoryPattern = true,
            IntraOpNumThreads = Environment.ProcessorCount,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            //InterOpNumThreads = Environment.ProcessorCount
        };

        // Register NNAPI GPU execution provider (Android Neural Networks API).
        // Heavy ops (Conv, MatMul, Softmax) run on GPU where available;
        // unsupported ops (LayerNorm, Gelu) fall back to CPU automatically.
        try
        {
            sessionOptions.AppendExecutionProvider_Nnapi(NnapiFlags.NNAPI_FLAG_CPU_ONLY);
        }
        catch
        {
            sessionOptions.AppendExecutionProvider_CPU();
        }

        return Task.Run(() => new InferenceSession(modelPath, sessionOptions), ct);
    }

    private static async Task<string> CacheModelFileAsync(CancellationToken ct)
    {
        var cachePath = Path.Combine(FileSystem.CacheDirectory, "model.onnx");

        if (!File.Exists(cachePath))
        {
            using var asset = await FileSystem.OpenAppPackageFileAsync("model.onnx");
            using var file = File.Create(cachePath);
            await asset.CopyToAsync(file, ct);
        }

        return cachePath;
    }

    private static BpeTokenizer BuildTokenizer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var model = root.GetProperty("model");

        var vocab = model.GetProperty("vocab");
        var vocabDict = new Dictionary<string, int>(51200);
        foreach (var entry in vocab.EnumerateObject())
            vocabDict[entry.Name] = entry.Value.GetInt32();

        var mergesList = new List<string>();
        foreach (var merge in model.GetProperty("merges").EnumerateArray())
        {
            if (merge.ValueKind == JsonValueKind.Array)
            {
                var parts = merge.EnumerateArray().Select(e => e.GetString()!).ToArray();
                mergesList.Add(string.Join(" ", parts));
            }
            else
            {
                mergesList.Add(merge.GetString()!);
            }
        }

        var options = new BpeOptions(vocabDict)
        {
            Merges = mergesList,
            PreTokenizer = new RobertaPreTokenizer(),
            Normalizer = new LowerCaseNormalizer(),
        };

        return BpeTokenizer.Create(options);
    }

    public async Task<double[]> GetImageEmbeddingAsync(string imagePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(_session);

        var pixels = _pixelBuffer.Value!;
        await Task.Run(() => PreprocessImage(imagePath, pixels), ct);

        var pixelTensor = new DenseTensor<float>(pixels, [1, 3, ImageSize, ImageSize]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", pixelTensor),
            NamedOnnxValue.CreateFromTensor("input_ids", DummyInputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", DummyAttentionMask)
        };

        using var results = _session.Run(inputs);
        var output = results.First(o => o.Name == "image_embeds");
        var outputTensor = output.AsTensor<float>();

        var hiddenDim = outputTensor.Dimensions[1];
        var raw = new float[hiddenDim];
        for (int i = 0; i < hiddenDim; i++)
            raw[i] = outputTensor[0, i];

        return Array.ConvertAll(raw, v => (double)v);
    }

    public Task<double[]> GetTextEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(_session);
        ArgumentNullException.ThrowIfNull(_tokenizer);

        var tokenIds = _tokenizer.EncodeToIds(text);
        var inputIds = _textInputIds.Value!;
        var attentionMask = _textAttentionMask.Value!;
        Array.Clear(inputIds);
        Array.Clear(attentionMask);

        inputIds[0] = BosTokenId;
        attentionMask[0] = 1;

        int pos = 1;
        for (int i = 0; i < tokenIds.Count && pos < MaxTokens - 1; i++)
        {
            inputIds[pos] = tokenIds[i];
            attentionMask[pos] = 1;
            pos++;
        }

        inputIds[pos] = EosTokenId;
        attentionMask[pos] = 1;

        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, MaxTokens]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, MaxTokens]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", DummyPixels),
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        using var results = _session.Run(inputs);
        var output = results.First(o => o.Name == "text_embeds");
        var outputTensor = output.AsTensor<float>();

        var hiddenDim = outputTensor.Dimensions[1];
        var raw = new float[hiddenDim];
        for (int i = 0; i < hiddenDim; i++)
            raw[i] = outputTensor[0, i];

        var embedding = Array.ConvertAll(raw, v => (double)v);
        return Task.FromResult(embedding);
    }

    private static void PreprocessImage(string imagePath, float[] pixelBuffer)
    {
        using var stream = OpenImageStream(imagePath);
        using var original = SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException($"Failed to decode image: {imagePath}");

        var info = new SKImageInfo(ImageSize, ImageSize, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var resized = original.Resize(info, sampling);

        if (resized == null)
            throw new InvalidOperationException("Failed to resize image");

        var pixels = resized.GetPixelSpan();
        var rowBytes = resized.RowBytes;

        for (int y = 0; y < ImageSize; y++)
        {
            var row = y * rowBytes;
            var rowOffset = y * ImageSize;
            for (int x = 0; x < ImageSize; x++)
            {
                var offset = row + x * 4;
                var idx = rowOffset + x;

                pixelBuffer[idx] = (pixels[offset + 2] * Inv255 - Mean[0]) / Std[0];
                pixelBuffer[ImageSize * ImageSize + idx] = (pixels[offset + 1] * Inv255 - Mean[1]) / Std[1];
                pixelBuffer[2 * ImageSize * ImageSize + idx] = (pixels[offset] * Inv255 - Mean[2]) / Std[2];
            }
        }
    }

    private static Stream OpenImageStream(string path)
    {
#if ANDROID
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = Android.Net.Uri.Parse(path);
            var context = Android.App.Application.Context;
            var stream = context.ContentResolver!.OpenInputStream(uri!);
            return stream ?? throw new InvalidOperationException($"Failed to open content URI: {path}");
        }
#endif
        return File.OpenRead(path);
    }

    private static async Task<string> ReadAssetStringAsync(string assetName, CancellationToken ct)
    {
        using var asset = await FileSystem.OpenAppPackageFileAsync(assetName);
        using var reader = new StreamReader(asset);
        return await reader.ReadToEndAsync(ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _pixelBuffer.Dispose();
        _textInputIds.Dispose();
        _textAttentionMask.Dispose();
    }
}
