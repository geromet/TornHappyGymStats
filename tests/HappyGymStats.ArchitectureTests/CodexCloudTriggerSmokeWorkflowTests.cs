using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HappyGymStats.ArchitectureTests;

// Contract for .github/workflows/codex-cloud-trigger-smoke.yml (issue #232, child
// of #227). The workflow posts one github-actions[bot] comment that invokes Codex
// Cloud; it must stay manual-only, narrowly permissioned, and free of any
// pull-request-code checkout/execution. These assertions fail the build if a
// later edit widens that trust surface.
public sealed class CodexCloudTriggerSmokeWorkflowTests
{
    private const string WorkflowPath = ".github/workflows/codex-cloud-trigger-smoke.yml";
    private const string PromptPath = ".github/agent/codex-cloud-trigger-smoke.prompt.md";

    [Fact]
    public void Workflow_is_manual_dispatch_only()
    {
        var workflow = ReadRepoFile(WorkflowPath);

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        // No automatic triggers: the diagnostic must never fire on repository events.
        Assert.DoesNotContain("\n  push:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  pull_request:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  pull_request_target:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  issues:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  issue_comment:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  schedule:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  repository_dispatch:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_grants_no_default_permissions_and_only_scoped_write_for_the_comment()
    {
        var workflow = ReadRepoFile(WorkflowPath);

        Assert.Contains("permissions: {}", workflow, StringComparison.Ordinal);
        Assert.Contains("pull-requests: write", workflow, StringComparison.Ordinal);
        Assert.Contains("issues: read", workflow, StringComparison.Ordinal);

        // The diagnostic must not acquire broad or dangerous scopes.
        Assert.DoesNotContain("contents: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("packages: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("write-all", workflow, StringComparison.Ordinal);

        // No repository secrets or provider API keys are wired in.
        Assert.DoesNotContain("${{ secrets.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workflow_never_checks_out_or_executes_pull_request_code()
    {
        var workflow = ReadRepoFile(WorkflowPath);

        Assert.DoesNotContain("actions/checkout", workflow, StringComparison.Ordinal);
        // The prompt template is read from the dispatched trusted ref, not a PR head.
        Assert.Contains("?ref=${TRUSTED_REF}", workflow, StringComparison.Ordinal);
        Assert.Contains("TRUSTED_REF: ${{ github.sha }}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_validates_inputs_and_fails_closed()
    {
        var workflow = ReadRepoFile(WorkflowPath);

        // Token charset guard.
        Assert.Contains("^[a-z0-9][a-z0-9-]{6,62}[a-z0-9]$", workflow, StringComparison.Ordinal);
        // Numeric PR guard.
        Assert.Contains("'^[1-9][0-9]{0,6}$'", workflow, StringComparison.Ordinal);
        // Target must be an open PR in this repo.
        Assert.Contains("is not an open pull request", workflow, StringComparison.Ordinal);
        // Coordination state must be readable or the run aborts.
        Assert.Contains("issues/140", workflow, StringComparison.Ordinal);
        Assert.Contains("fail closed", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_deduplicates_on_token_and_bounds_the_run()
    {
        var workflow = ReadRepoFile(WorkflowPath);

        Assert.Contains("id: dedup", workflow, StringComparison.Ordinal);
        Assert.Contains("skip=true", workflow, StringComparison.Ordinal);
        Assert.Contains("--paginate", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"timeout-minutes:\s*\d+"), workflow);
        Assert.Contains("group: codex-cloud-trigger-smoke", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_posts_the_body_as_structured_data_not_shell_interpolation()
    {
        var workflow = ReadRepoFile(WorkflowPath);

        // Body composed from a file via jq --rawfile, sent through gh api --input -,
        // so no issue/PR text is ever spliced into a shell command.
        Assert.Contains("jq -n --rawfile body", workflow, StringComparison.Ordinal);
        Assert.Contains("--input -", workflow, StringComparison.Ordinal);
        Assert.Contains("expected 'github-actions[bot]'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_template_is_fixed_read_only_and_token_parameterised()
    {
        var prompt = ReadRepoFile(PromptPath);

        Assert.Contains("__DIAGNOSTIC_TOKEN__", prompt, StringComparison.Ordinal);
        Assert.Contains("read-only Cloud trigger smoke test", prompt, StringComparison.Ordinal);
        Assert.Contains("issue #227", prompt, StringComparison.Ordinal);

        // The prompt must forbid mutation and secret exposure by the worker.
        Assert.Contains("Do not implement or review this PR", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not dump environment", prompt, StringComparison.Ordinal);
        Assert.Contains("Stop after reporting.", prompt, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root from test output directory.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
