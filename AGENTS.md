# ImageVault — Agent Guide

A .NET MAUI Android app for semantic image search using CLIP embeddings (ONNX) + in-memory vector DB.

## Quick start

```powershell
dotnet build -f net10.0-android              # debug
dotnet build -f net10.0-android -c Release   # produces APK (AOT off)
```

**`net10.0-android` only** — no iOS/Windows/macOS. No test project exists. No codegen or migrations.

## Architecture

| Layer | Key files | Notes |
|---|---|---|
| DI wiring | `MauiProgram.cs` | 3 singleton services + singleton MainViewModel/MainPage + transient ImageViewerPage. Calls `.UseSkiaSharp()`, `.UseMauiCommunityToolkit()`. |
| Entry | `App.xaml.cs` → `AppShell.xaml` (flyout disabled) → `MainPage.xaml` | Shell route `"viewer"` registered for ImageViewerPage, accepts `path` query param. |
| ViewModel | `MainViewModel.cs` | CommunityToolkit.Mvvm source generators. `InitializeCommand` fires on first page appear. `ImportDirectoryCommand(directoryPath)` and `PickImagesCommand` for ingestion. `SearchCommand` triggers only when `SearchQuery` non-empty; clearing the query empties results and falls back to gallery. |
| Image embedding | `ClipService.cs` | Unified `model.onnx` (clip-vit-large-patch14-ONNX) + `tokenizer.json` from `Resources/Raw`. Model (~1.7GB) is copied from APK assets to `FileSystem.CacheDirectory` on first load. Single ONNX session with `pixel_values`/`input_ids`/`attention_mask` inputs, `image_embeds`/`text_embeds` outputs. SkiaSharp preprocesses 224×224 with CLIP normalisation. `#if ANDROID` branch for `content://` URI streams. Uses NNAPI GPU execution provider (`AppendExecutionProvider_Nnapi` with FP16) — falls back to CPU if unavailable. Dummy/constant tensors pre-allocated as `static`; dynamic pixel/text buffers use `ThreadLocal` to avoid per-call `float[150528]` and `long[77]` allocations. `DenseTensor` wraps existing arrays via `DenseTensor(T[], dimensions)` without copying. |
| Vector storage | `ImageVectorDatabase.cs` wraps `Build5Nines.SharpVector.MemoryVectorDatabase<string>` | Cosine similarity + softmax + logit score. Embeddings stored as `float[]` internally; service layer converts to/from `double[]` at boundaries. `VectorDbService` persists to `imagevault.b59vdb` in `FileSystem.AppDataDirectory` on **every** insert/clear — performance consideration for large batches. |
| Batch processing | `ImageProcessingService.cs` | Channel (capacity 8) + `Parallel.ForEachAsync` with `Environment.ProcessorCount` DOP. Deduplicates by path. Skips unsupported extensions (jpg/jpeg/png/webp/gif/bmp). Has static `ScanDirectory(directoryPath)` helper. |

## Key constraints

- **CLIP model is large** (`Resources/Raw/model.onnx` ~1.7GB). Not committed? Check LFS/gitignore. Also `tokenizer.json` (~3.6MB).
- **Model loading is async** on first page appear via `InitializeCommand`. Model load runs in background while UI shows loading spinner/progress. `IsModelLoaded` guards re-initialization.
- **Android permissions** in `Platforms/Android/AndroidManifest.xml`: `INTERNET`, `READ_EXTERNAL_STORAGE` (maxSdkVersion=32), `READ_MEDIA_IMAGES`, `ACCESS_NETWORK_STATE`.
- `.onnx` files stored uncompressed in APK (`AndroidStoreUncompressedFileExtensions`).
- `ImageProcessingService.OnItemProcessed` event handler is a no-op in the constructor (intended for future use).
