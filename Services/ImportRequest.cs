using ImageVault.Models;

namespace ImageVault.Services;

public class ImportRequest
{
    public string? DirectoryPath { get; set; }
    public List<ProcessingItem>? PickedFiles { get; set; }
}
