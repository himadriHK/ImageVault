namespace ImageVault.Models;

public class SearchResult
{
    public ImageEntity Entity { get; set; } = null!;
    public double Score { get; set; }
    public double DotProduct { get; set; }
    public double LogitScore { get; set; }
}
