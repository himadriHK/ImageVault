using Microsoft.ML.OnnxRuntime;

namespace ImageVault.Services;

public static class OrtOptimizer
{
    public static List<string> EnabledProviders { get; } = [];

    public static SessionOptions CreateOptimizedOptions()
    {
        EnabledProviders.Clear();

        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = true,
            IntraOpNumThreads = Environment.ProcessorCount,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
        };

        EnabledProviders.Add("CPU");
        return opts;
    }
}
