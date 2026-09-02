using Engineering_IntelligenceTools.Models.GitHub;

namespace Engineering_IntelligenceTools.Models.Analysis;
public class AnalysisContext
{
    //Amit this is for testing
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public string RepoFullName => $"{Owner}/{Repo}";
    public int? PullRequestNumber { get; init; }
    public required string BaseSha { get; init; }
    public required string HeadSha { get; init; }
    public required IReadOnlyList<ChangedFile> Files { get; init; }
}
