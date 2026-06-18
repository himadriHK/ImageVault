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
    private InferenceSession? _imageSession;
    private InferenceSession? _textSession;
    private bool _disposed;

    private const int ImageSize = 224;
    private const int MaxTokens = 77;
    private const int BosTokenId = 49406;
    private const int EosTokenId = 49407;
    private const float Inv255 = 1f / 255f;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    public bool IsModelLoaded => _imageSession != null && _textSession != null;

    public async Task LoadModelsAsync(CancellationToken ct = default)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
            EnableCpuMemArena = true,
            IntraOpNumThreads = Environment.ProcessorCount,
            ExecutionMode = ExecutionMode.ORT_PARALLEL
        };

        var imageBytes = await ReadAssetBytesAsync("clip_image_int8.onnx", ct);
        var textBytes = await ReadAssetBytesAsync("clip_text_int8.onnx", ct);
        var tokenizerJson = await ReadAssetStringAsync("tokenizer.json", ct);

        _tokenizer = await Task.Run(() => BuildTokenizer(tokenizerJson), ct);

        _imageSession = await Task.Run(
            () => new InferenceSession(imageBytes, sessionOptions), ct);
        _textSession = await Task.Run(
            () => new InferenceSession(textBytes, sessionOptions), ct);
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
            mergesList.Add(merge.GetString()!);

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
        ArgumentNullException.ThrowIfNull(_imageSession);

        var tensor = await Task.Run(() => LoadAndPreprocessImage(imagePath), ct);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", tensor)
        };

        using var results = _imageSession.Run(inputs);
        var output = results.First();
        var outputTensor = output.AsTensor<float>();

        float[] raw;
        if (outputTensor.Dimensions.Length == 1)
        {
            var dim = outputTensor.Dimensions[0];
            var flat = outputTensor.ToArray();
            raw = new float[dim];
            Array.Copy(flat, 0, raw, 0, dim);
        }
        else if (outputTensor.Dimensions.Length == 2)
        {
            var hiddenDim = outputTensor.Dimensions[1];
            raw = new float[hiddenDim];
            for (int i = 0; i < hiddenDim; i++)
                raw[i] = outputTensor[0, i];
        }
        else
        {
            var hiddenDim = outputTensor.Dimensions[^1];
            raw = new float[hiddenDim];
            for (int i = 0; i < hiddenDim; i++)
                raw[i] = outputTensor[0, 0, i];
        }

        var embedding = Array.ConvertAll(raw, v => (double)v);
        return NormalizeEmbedding(embedding);
    }

    public Task<double[]> GetTextEmbeddingAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(_textSession);
        ArgumentNullException.ThrowIfNull(_tokenizer);

        var tokenIds = _tokenizer.EncodeToIds(text);
        var ids = new List<long>(MaxTokens) { BosTokenId };

        for (int i = 0; i < tokenIds.Count && ids.Count < MaxTokens - 1; i++)
            ids.Add(tokenIds[i]);

        ids.Add(EosTokenId);

        while (ids.Count < MaxTokens)
            ids.Add(0);

        var inputIds = new DenseTensor<long>(new[] { 1, MaxTokens });
        for (int i = 0; i < MaxTokens; i++)
            inputIds[0, i] = ids[i];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds)
        };

        if (_textSession.InputMetadata.ContainsKey("attention_mask"))
        {
            var attentionMask = new DenseTensor<long>(new[] { 1, MaxTokens });
            for (int i = 0; i < MaxTokens; i++)
                attentionMask[0, i] = ids[i] != 0 ? 1 : 0;
            inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask));
        }

        using var results = _textSession.Run(inputs);
        var output = results.First();
        var outputTensor = output.AsTensor<float>();
        var eosIdx = Math.Min(tokenIds.Count, MaxTokens - 2) + 1;

        float[] raw;
        if (outputTensor.Dimensions.Length == 1)
        {
            var hiddenDim = outputTensor.Dimensions[0];
            var flat = outputTensor.ToArray();
            raw = new float[hiddenDim];
            Array.Copy(flat, 0, raw, 0, hiddenDim);
        }
        else if (outputTensor.Dimensions.Length == 2)
        {
            var hiddenDim = outputTensor.Dimensions[1];
            raw = new float[hiddenDim];
            for (int i = 0; i < hiddenDim; i++)
                raw[i] = outputTensor[0, i];
        }
        else
        {
            var hiddenDim = outputTensor.Dimensions[^1];
            raw = new float[hiddenDim];
            for (int i = 0; i < hiddenDim; i++)
                raw[i] = outputTensor[0, eosIdx, i];
        }

        var embedding = Array.ConvertAll(raw, v => (double)v);
        return Task.FromResult(NormalizeEmbedding(embedding));
    }

    private static Tensor<float> LoadAndPreprocessImage(string imagePath)
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
        var tensor = new DenseTensor<float>(new[] { 1, 3, ImageSize, ImageSize });

        for (int y = 0; y < ImageSize; y++)
        {
            var row = y * rowBytes;
            for (int x = 0; x < ImageSize; x++)
            {
                var offset = row + x * 4;

                tensor[0, 0, y, x] = (pixels[offset + 2] * Inv255 - Mean[0]) / Std[0];
                tensor[0, 1, y, x] = (pixels[offset + 1] * Inv255 - Mean[1]) / Std[1];
                tensor[0, 2, y, x] = (pixels[offset] * Inv255 - Mean[2]) / Std[2];
            }
        }

        return tensor;
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

    private static double[] NormalizeEmbedding(double[] embedding)
    {
        var sumSq = embedding.Sum(v => v * v);
        var norm = Math.Sqrt(sumSq);
        if (norm < 1e-12) return embedding;

        var normalized = new double[embedding.Length];
        for (int i = 0; i < embedding.Length; i++)
            normalized[i] = embedding[i] / norm;
        return normalized;
    }

    private static async Task<byte[]> ReadAssetBytesAsync(string assetName, CancellationToken ct)
    {
        using var asset = await FileSystem.OpenAppPackageFileAsync(assetName);
        using var memory = new MemoryStream();
        await asset.CopyToAsync(memory, ct);
        return memory.ToArray();
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
        _imageSession?.Dispose();
        _textSession?.Dispose();
    }
}
