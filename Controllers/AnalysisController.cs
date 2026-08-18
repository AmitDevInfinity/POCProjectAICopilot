using Engineering_IntelligenceTools.Models.Analysis;
using Engineering_IntelligenceTools.Services.Interfaces;
using Engineering_IntelligenceTools.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace Engineering_IntelligenceTools.Controllers;

[ApiController]
[Route("api/analysis")]
public class AnalysisController : ControllerBase
{
    private readonly IGitHubClientService _gitHubClientService;
    private readonly IAnalyzerOrchestrator _orchestrator;
    private readonly IAnalysisResultStore _resultStore;
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(
        IGitHubClientService gitHubClientService,
        IAnalyzerOrchestrator orchestrator,
          IAnalysisResultStore resultStore,
        ILogger<AnalysisController> logger)
    {
        _gitHubClientService = gitHubClientService;
        _orchestrator = orchestrator;
        _resultStore = resultStore;
        _logger = logger;
    }


    [HttpPost("run")]
    [ProducesResponseType(typeof(AnalysisResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Run([FromBody] AnalysisRequest request, CancellationToken cancellationToken)
    {
        string owner;
        string repo;
        int prNumber;

        if (GitHubUrlParser.TryParse(request.Url, out var prOwner, out var prRepo, out var parsedPrNumber))
        {
            // A specific PR URL was given - analyze exactly that PR.
            owner = prOwner;
            repo = prRepo;
            prNumber = parsedPrNumber;
        }
        else if (GitHubRepositoryUrlParser.TryParse(request.Url, out var repoOwner, out var repoName))
        {
            // A plain repo URL was given - auto-pick the latest open PR.
            owner = repoOwner;
            repo = repoName;

            int? latestPrNumber;
            try
            {
                latestPrNumber = await _gitHubClientService.GetLatestOpenPullRequestNumberAsync(owner, repo, cancellationToken);
            }
            catch (Octokit.NotFoundException)
            {
                return NotFound(new { error = $"Repository not found: {owner}/{repo}" });
            }

            if (latestPrNumber is null)
            {
                return NotFound(new
                {
                    error = $"No open pull requests found on {owner}/{repo}. Open a PR first, then try again."
                });
            }

            prNumber = latestPrNumber.Value;
        }
        else
        {
            return BadRequest(new
            {
                error = "url is not a recognized GitHub URL. Expected either " +
                        "https://github.com/{owner}/{repo}/pull/{number} or " +
                        "https://github.com/{owner}/{repo}(.git)"
            });
        }

        try
        {
            var files = await _gitHubClientService.GetPullRequestFilesAsync(owner, repo, prNumber, cancellationToken);
            var (baseSha, headSha) = await _gitHubClientService.GetPullRequestShaRangeAsync(owner, repo, prNumber, cancellationToken);

            var context = new AnalysisContext
            {
                Owner = owner,
                Repo = repo,
                PullRequestNumber = prNumber,
                BaseSha = baseSha,
                HeadSha = headSha,
                Files = files
            };

            var result = await _orchestrator.AnalyzeAsync(context, cancellationToken);
            _resultStore.Save(result);

            return Ok(result);
        }
        catch (Octokit.NotFoundException)
        {
            return NotFound(new { error = $"Repository or PR not found: {owner}/{repo} #{prNumber}" });
        }
        catch (Octokit.ForbiddenException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "GitHub API access forbidden - check the configured access token/permissions."
            });
        }
    }

    [HttpGet("{owner}/{repo}")]
    [ProducesResponseType(typeof(AnalysisResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLatest(string owner, string repo, [FromQuery] int? pullRequestNumber)
    {
        var result = _resultStore.Get($"{owner}/{repo}", pullRequestNumber);
        return result is null
            ? NotFound(new { error = "No analysis found for this repo/PR yet." })
            : Ok(result);
    }
}
