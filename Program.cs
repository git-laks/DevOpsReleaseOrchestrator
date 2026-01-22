using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace DevOpsReleaseOrchestrator;

#region Configuration Models

public class AppSettings
{
    public string OrganizationUrl { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public List<string> SelectableStages { get; set; } = [];
    public List<CancellationRule> CancellationRules { get; set; } = [];
}

public class CancellationRule
{
    public string TriggerStage { get; set; } = string.Empty;
    public string ObsoleteStage { get; set; } = string.Empty;
}

#endregion

#region Azure DevOps API Models

public class ApprovalsResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("value")]
    public List<Approval> Value { get; set; } = [];
}

public class Approval
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<ApprovalStep>? Steps { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createdOn")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("pipeline")]
    public PipelineReference? Pipeline { get; set; }

    [JsonPropertyName("resource")]
    public ApprovalResource? Resource { get; set; }
}

public class ApprovalStep
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("assignedApprover")]
    public IdentityRef? AssignedApprover { get; set; }

    [JsonPropertyName("actualApprover")]
    public IdentityRef? ActualApprover { get; set; }
}

public class ApprovalResource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class PipelineReference
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class IdentityRef
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("uniqueName")]
    public string UniqueName { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class BuildsResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("value")]
    public List<Build> Value { get; set; } = [];
}

public class Build
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("buildNumber")]
    public string BuildNumber { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("definition")]
    public BuildDefinition? Definition { get; set; }

    [JsonPropertyName("queueTime")]
    public DateTime QueueTime { get; set; }

    [JsonPropertyName("sourceBranch")]
    public string SourceBranch { get; set; } = string.Empty;

    [JsonPropertyName("_links")]
    public BuildLinks? Links { get; set; }
}

public class BuildDefinition
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class BuildLinks
{
    [JsonPropertyName("web")]
    public LinkReference? Web { get; set; }
}

public class LinkReference
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;
}

public class TimelineResponse
{
    [JsonPropertyName("records")]
    public List<TimelineRecord> Records { get; set; } = [];
}

public class TimelineRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string? Result { get; set; }
}

public class ApprovalUpdateRequest
{
    [JsonPropertyName("approvalId")]
    public string ApprovalId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;
}

public class ConnectionDataResponse
{
    [JsonPropertyName("authenticatedUser")]
    public IdentityRef? AuthenticatedUser { get; set; }
}

#endregion

#region Display Models

public class ApprovalDisplayItem
{
    public string ApprovalId { get; set; } = string.Empty;
    public string PipelineName { get; set; } = string.Empty;
    public int PipelineId { get; set; }
    public int BuildId { get; set; }
    public string BuildNumber { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}

public class ObsoleteBuildItem
{
    public int BuildId { get; set; }
    public string BuildNumber { get; set; } = string.Empty;
    public string PipelineName { get; set; } = string.Empty;
    public int PipelineId { get; set; }
    public string CurrentStage { get; set; } = string.Empty;
    public DateTime QueueTime { get; set; }
}

#endregion

public class DevOpsOrchestrator
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly string _baseUrl;
    private string _currentUserDisplayName = string.Empty;
    private string _currentUserEmailPrefix = string.Empty;

    public bool HasValidUser => !string.IsNullOrEmpty(_currentUserEmailPrefix);

    public DevOpsOrchestrator(HttpClient httpClient, AppSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _baseUrl = $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.ProjectName}/_apis";
    }

    public async Task<string> GetCurrentUserIdAsync()
    {
        try
        {
            // Use the VSSPS profile API which works reliably with Azure AD tokens
            var profileUrl = "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1-preview.3";
            var profileResponse = await _httpClient.GetAsync(profileUrl);

            if (profileResponse.IsSuccessStatusCode)
            {
                var profileJson = await profileResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(profileJson);

                if (doc.RootElement.TryGetProperty("displayName", out var nameProp))
                {
                    _currentUserDisplayName = nameProp.GetString() ?? string.Empty;
                }

                // Extract email prefix for matching (more reliable than display name)
                if (doc.RootElement.TryGetProperty("emailAddress", out var emailProp))
                {
                    var email = emailProp.GetString() ?? string.Empty;
                    _currentUserEmailPrefix = GetEmailPrefix(email);
                }

                if (!string.IsNullOrEmpty(_currentUserDisplayName))
                {
                    return _currentUserDisplayName;
                }
            }

            // Fallback to connectionData API if profile API fails
            var url = $"{_settings.OrganizationUrl.TrimEnd('/')}/_apis/connectionData?api-version=7.1";
            var response = await _httpClient.GetFromJsonAsync<ConnectionDataResponse>(url);
            _currentUserDisplayName = response?.AuthenticatedUser?.DisplayName ?? string.Empty;
            _currentUserEmailPrefix = GetEmailPrefix(response?.AuthenticatedUser?.UniqueName ?? string.Empty);
            return _currentUserDisplayName.Length > 0 ? _currentUserDisplayName : "Unknown User";
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Could not fetch user info: {ex.Message}[/]");
            return "Unknown User";
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Could not parse user info: {ex.Message}[/]");
            return "Unknown User";
        }
    }

    private static string GetEmailPrefix(string email)
    {
        if (string.IsNullOrEmpty(email)) return string.Empty;
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[..atIndex] : email;
    }

    public async Task<List<ApprovalDisplayItem>> GetPendingApprovalsForStageAsync(string stageName)
    {
        var results = new List<ApprovalDisplayItem>();

        try
        {
            var url = $"{_baseUrl}/pipelines/approvals?api-version=7.1&state=pending&$expand=steps";
            var response = await _httpClient.GetFromJsonAsync<ApprovalsResponse>(url);

            if (response?.Value == null) return results;

            foreach (var approval in response.Value)
            {
                // Check if current user is assigned to this approval
                // Match by email prefix since IDs and full emails may differ across systems
                bool isAssigned;
                if (string.IsNullOrEmpty(_currentUserEmailPrefix))
                {
                    // If we couldn't get the user info, show all pending approvals
                    isAssigned = approval.Steps?.Any(s =>
                        s.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)) ?? false;
                }
                else
                {
                    isAssigned = approval.Steps?.Any(s =>
                        s.Status.Equals("pending", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(GetEmailPrefix(s.AssignedApprover?.UniqueName ?? ""), _currentUserEmailPrefix, StringComparison.OrdinalIgnoreCase)) ?? false;
                }

                if (!isAssigned) continue;

                // Check if this approval is for the selected stage
                var resourceName = approval.Resource?.Name ?? string.Empty;
                if (!resourceName.Contains(stageName, StringComparison.OrdinalIgnoreCase)) continue;

                // Get build info from the approval
                var buildId = 0;
                var buildNumber = "N/A";

                if (approval.Resource?.Type == "environment")
                {
                    // For environment approvals, we need to find the associated run
                    var buildInfo = await GetBuildInfoForApprovalAsync(approval);
                    buildId = buildInfo.buildId;
                    buildNumber = buildInfo.buildNumber;
                }

                results.Add(new ApprovalDisplayItem
                {
                    ApprovalId = approval.Id,
                    PipelineName = approval.Pipeline?.Name ?? "Unknown",
                    PipelineId = approval.Pipeline?.Id ?? 0,
                    BuildId = buildId,
                    BuildNumber = buildNumber,
                    StageName = resourceName,
                    CreatedOn = approval.CreatedOn
                });
            }
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error fetching approvals: {ex.Message}[/]");
        }

        return results;
    }

    private async Task<(int buildId, string buildNumber)> GetBuildInfoForApprovalAsync(Approval approval)
    {
        try
        {
            if (approval.Pipeline == null) return (0, "N/A");

            // Get recent runs for this pipeline
            var url = $"{_baseUrl}/pipelines/{approval.Pipeline.Id}/runs?api-version=7.1&$top=10";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return (0, "N/A");

            var runsJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(runsJson);

            if (doc.RootElement.TryGetProperty("value", out var runs))
            {
                foreach (var run in runs.EnumerateArray())
                {
                    if (run.TryGetProperty("state", out var state) &&
                        state.GetString()?.Equals("inProgress", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var id = run.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                        var name = run.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "N/A" : "N/A";
                        return (id, name);
                    }
                }
            }
        }
        catch
        {
            // Ignore errors and return default
        }

        return (0, "N/A");
    }

    public async Task<List<ObsoleteBuildItem>> FindObsoleteBuildsAsync(
        List<ApprovalDisplayItem> approvingItems,
        string obsoleteStage)
    {
        var obsoleteBuilds = new List<ObsoleteBuildItem>();

        // Get unique pipeline IDs from items being approved
        var pipelineIds = approvingItems.Select(a => a.PipelineId).Distinct().ToList();

        foreach (var pipelineId in pipelineIds)
        {
            try
            {
                // Get in-progress builds for this pipeline definition
                var url = $"{_baseUrl}/build/builds?api-version=7.1&definitions={pipelineId}&statusFilter=inProgress";
                var response = await _httpClient.GetFromJsonAsync<BuildsResponse>(url);

                if (response?.Value == null) continue;

                foreach (var build in response.Value)
                {
                    // Skip builds that are being approved
                    if (approvingItems.Any(a => a.BuildId == build.Id)) continue;

                    // Check if this build is currently at the obsolete stage
                    var currentStage = await GetBuildCurrentStageAsync(build.Id);

                    if (currentStage.Contains(obsoleteStage, StringComparison.OrdinalIgnoreCase))
                    {
                        obsoleteBuilds.Add(new ObsoleteBuildItem
                        {
                            BuildId = build.Id,
                            BuildNumber = build.BuildNumber,
                            PipelineName = build.Definition?.Name ?? "Unknown",
                            PipelineId = pipelineId,
                            CurrentStage = currentStage,
                            QueueTime = build.QueueTime
                        });
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not check builds for pipeline {pipelineId}: {ex.Message}[/]");
            }
        }

        return obsoleteBuilds;
    }

    private async Task<string> GetBuildCurrentStageAsync(int buildId)
    {
        try
        {
            var url = $"{_baseUrl}/build/builds/{buildId}/timeline?api-version=7.1";
            var response = await _httpClient.GetFromJsonAsync<TimelineResponse>(url);

            if (response?.Records == null) return "Unknown";

            // Find stages (type = "Stage") that are in progress or pending
            var stages = response.Records
                .Where(r => r.Type.Equals("Stage", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Find the stage that's currently running or pending
            var currentStage = stages.FirstOrDefault(s =>
                s.State.Equals("inProgress", StringComparison.OrdinalIgnoreCase) ||
                s.State.Equals("pending", StringComparison.OrdinalIgnoreCase));

            return currentStage?.Name ?? stages.LastOrDefault()?.Name ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task<bool> CancelBuildAsync(int buildId)
    {
        try
        {
            var url = $"{_baseUrl}/build/builds/{buildId}?api-version=7.1";
            var content = JsonContent.Create(new { status = "cancelling" });
            var response = await _httpClient.PatchAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error cancelling build {buildId}: {ex.Message}[/]");
            return false;
        }
    }

    public async Task<bool> ApproveAsync(string approvalId, string comment = "Approved via Release Orchestrator")
    {
        try
        {
            var url = $"{_baseUrl}/pipelines/approvals?api-version=7.1";

            var payload = new[]
            {
                new
                {
                    approvalId = approvalId,
                    status = 4, // 4 = Approved
                    comment = comment
                }
            };

            var response = await _httpClient.PatchAsJsonAsync(url, payload);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error approving {approvalId}: {ex.Message}[/]");
            return false;
        }
    }
}

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("DevOps Orchestrator")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[grey]Azure DevOps Release Orchestrator v1.0[/]");
        AnsiConsole.WriteLine();

        // Step 1: Load Configuration
        AppSettings? settings = null;
        await AnsiConsole.Status()
            .StartAsync("[yellow]Loading configuration...[/]", async ctx =>
            {
                settings = LoadConfiguration();
                await Task.CompletedTask;
            });

        if (settings == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to load configuration. Exiting.[/]");
            return 1;
        }

        DisplayConfigurationSummary(settings);

        // Prompt user before launching browser for authentication
        AnsiConsole.MarkupLine("[yellow]Authentication required.[/] Press [green]Enter[/] to open browser and sign in with Azure AD...");
        Console.ReadLine();

        // Step 2: Authenticate
        TokenCredential? credential = null;
        HttpClient? httpClient = null;
        DevOpsOrchestrator? orchestrator = null;
        string userName = string.Empty;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Authenticating with Azure AD...[/]", async ctx =>
                {
                    credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                    {
                        TokenCachePersistenceOptions = new TokenCachePersistenceOptions()
                    });

                    // Get token for Azure DevOps
                    var tokenRequest = new TokenRequestContext(["499b84ac-1321-427f-aa17-267ca6975798/.default"]);
                    var token = await credential.GetTokenAsync(tokenRequest, CancellationToken.None);

                    httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token.Token);
                    httpClient.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json"));

                    orchestrator = new DevOpsOrchestrator(httpClient, settings);

                    ctx.Status("[yellow]Fetching user information...[/]");
                    userName = await orchestrator.GetCurrentUserIdAsync();
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Authenticated as: [cyan]{userName}[/]");
            if (!orchestrator!.HasValidUser)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Warning: Could not retrieve your user info. Showing all pending approvals instead of filtering by assignment.[/]");
            }
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Authentication failed: {ex.Message}[/]");
            return 1;
        }

        // Main application loop
        while (true)
        {
            // Step 3: User selects a Stage
            var selectedStage = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Select a stage to approve:[/]")
                    .PageSize(10)
                    .AddChoices(settings.SelectableStages.Append("Exit").ToArray()));

            if (selectedStage == "Exit")
            {
                AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                break;
            }

            AnsiConsole.WriteLine();

            // Step 4: Query for pending approvals
            List<ApprovalDisplayItem> pendingApprovals = [];
            List<ObsoleteBuildItem> obsoleteBuilds = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"[yellow]Searching for pending approvals in '{selectedStage}'...[/]", async ctx =>
                {
                    pendingApprovals = await orchestrator!.GetPendingApprovalsForStageAsync(selectedStage);

                    if (pendingApprovals.Count == 0) return;

                    // Step 5: Dynamic Cleanup Check
                    var matchingRule = settings.CancellationRules
                        .FirstOrDefault(r => r.TriggerStage.Equals(selectedStage, StringComparison.OrdinalIgnoreCase));

                    if (matchingRule != null)
                    {
                        ctx.Status($"[yellow]Checking for obsolete builds in '{matchingRule.ObsoleteStage}'...[/]");
                        obsoleteBuilds = await orchestrator.FindObsoleteBuildsAsync(
                            pendingApprovals,
                            matchingRule.ObsoleteStage);
                    }
                });

            // Step 6: Display summary
            if (pendingApprovals.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No pending approvals found for stage '{selectedStage}'.[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            // Display Builds to Approve
            AnsiConsole.MarkupLine("[green]═══ BUILDS TO APPROVE ═══[/]");
            var approvalTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("[cyan]Pipeline[/]").Centered())
                .AddColumn(new TableColumn("[cyan]Build #[/]").Centered())
                .AddColumn(new TableColumn("[cyan]Stage[/]").Centered())
                .AddColumn(new TableColumn("[cyan]Pending Since[/]").Centered());

            foreach (var item in pendingApprovals)
            {
                approvalTable.AddRow(
                    item.PipelineName,
                    item.BuildNumber,
                    item.StageName,
                    item.CreatedOn.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            }

            AnsiConsole.Write(approvalTable);
            AnsiConsole.WriteLine();

            // Display Obsolete Builds to Cancel (if any)
            if (obsoleteBuilds.Count > 0)
            {
                AnsiConsole.MarkupLine("[red]═══ OBSOLETE BUILDS TO CANCEL ═══[/]");
                var cancelTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn(new TableColumn("[yellow]Pipeline[/]").Centered())
                    .AddColumn(new TableColumn("[yellow]Build #[/]").Centered())
                    .AddColumn(new TableColumn("[yellow]Current Stage[/]").Centered())
                    .AddColumn(new TableColumn("[yellow]Queued[/]").Centered());

                foreach (var item in obsoleteBuilds)
                {
                    cancelTable.AddRow(
                        item.PipelineName,
                        item.BuildNumber,
                        item.CurrentStage,
                        item.QueueTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                }

                AnsiConsole.Write(cancelTable);
                AnsiConsole.WriteLine();
            }

            // Summary
            var summaryPanel = new Panel(
                $"[green]Approvals:[/] {pendingApprovals.Count}  |  [red]Cancellations:[/] {obsoleteBuilds.Count}")
                .Header("[cyan]Summary[/]")
                .BorderColor(Color.Grey);
            AnsiConsole.Write(summaryPanel);
            AnsiConsole.WriteLine();

            // Step 7: Ask for confirmation
            if (!AnsiConsole.Confirm("[yellow]Do you want to proceed with these actions?[/]"))
            {
                AnsiConsole.MarkupLine("[grey]Operation cancelled by user.[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            // Step 8: Execute - Cancel obsolete builds first, then approve
            AnsiConsole.WriteLine();

            await AnsiConsole.Progress()
                .Columns(
                [
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                ])
                .StartAsync(async ctx =>
                {
                    // Cancel obsolete builds
                    if (obsoleteBuilds.Count > 0)
                    {
                        var cancelTask = ctx.AddTask("[red]Cancelling obsolete builds[/]", maxValue: obsoleteBuilds.Count);

                        foreach (var build in obsoleteBuilds)
                        {
                            var success = await orchestrator!.CancelBuildAsync(build.BuildId);
                            if (success)
                            {
                                AnsiConsole.MarkupLine($"  [red]✗[/] Cancelled: {build.PipelineName} #{build.BuildNumber}");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"  [yellow]![/] Failed to cancel: {build.PipelineName} #{build.BuildNumber}");
                            }
                            cancelTask.Increment(1);
                        }
                    }

                    // Approve new builds
                    var approveTask = ctx.AddTask("[green]Approving builds[/]", maxValue: pendingApprovals.Count);

                    foreach (var approval in pendingApprovals)
                    {
                        var success = await orchestrator!.ApproveAsync(approval.ApprovalId);
                        if (success)
                        {
                            AnsiConsole.MarkupLine($"  [green]✓[/] Approved: {approval.PipelineName} #{approval.BuildNumber}");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"  [yellow]![/] Failed to approve: {approval.PipelineName} #{approval.BuildNumber}");
                        }
                        approveTask.Increment(1);
                    }
                });

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Operation completed![/]");
            AnsiConsole.WriteLine();

            // Ask if user wants to continue
            if (!AnsiConsole.Confirm("[grey]Do you want to perform another operation?[/]"))
            {
                AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                break;
            }

            AnsiConsole.WriteLine();
        }

        httpClient?.Dispose();
        return 0;
    }

    private static AppSettings? LoadConfiguration()
    {
        try
        {
            var exeDirectory = AppContext.BaseDirectory;
            var configPath = Path.Combine(exeDirectory, "appsettings.json");

            if (!File.Exists(configPath))
            {
                AnsiConsole.MarkupLine($"[red]Configuration file not found at: {configPath}[/]");
                return null;
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(exeDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var settings = new AppSettings();
            configuration.Bind(settings);

            // Validate configuration
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(settings.OrganizationUrl))
                errors.Add("OrganizationUrl is required");

            if (string.IsNullOrWhiteSpace(settings.ProjectName))
                errors.Add("ProjectName is required");

            if (settings.SelectableStages.Count == 0)
                errors.Add("SelectableStages must contain at least one stage");

            foreach (var rule in settings.CancellationRules)
            {
                if (string.IsNullOrWhiteSpace(rule.TriggerStage))
                    errors.Add("CancellationRule.TriggerStage cannot be empty");
                if (string.IsNullOrWhiteSpace(rule.ObsoleteStage))
                    errors.Add("CancellationRule.ObsoleteStage cannot be empty");
            }

            if (errors.Count > 0)
            {
                AnsiConsole.MarkupLine("[red]Configuration validation errors:[/]");
                foreach (var error in errors)
                {
                    AnsiConsole.MarkupLine($"  [red]•[/] {error}");
                }
                return null;
            }

            return settings;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error loading configuration: {ex.Message}[/]");
            return null;
        }
    }

    private static void DisplayConfigurationSummary(AppSettings settings)
    {
        var configTable = new Table()
            .Border(TableBorder.Rounded)
            .Title("[cyan]Configuration Summary[/]")
            .AddColumn("Setting")
            .AddColumn("Value");

        configTable.AddRow("Organization", settings.OrganizationUrl);
        configTable.AddRow("Project", settings.ProjectName);
        configTable.AddRow("Selectable Stages", string.Join(", ", settings.SelectableStages));
        configTable.AddRow("Cancellation Rules", settings.CancellationRules.Count.ToString());

        AnsiConsole.Write(configTable);
        AnsiConsole.WriteLine();

        if (settings.CancellationRules.Count > 0)
        {
            var rulesTable = new Table()
                .Border(TableBorder.Simple)
                .Title("[yellow]Cancellation Rules[/]")
                .AddColumn("When Approving (Trigger)")
                .AddColumn("Cancel Builds In (Obsolete)");

            foreach (var rule in settings.CancellationRules)
            {
                rulesTable.AddRow(rule.TriggerStage, rule.ObsoleteStage);
            }

            AnsiConsole.Write(rulesTable);
            AnsiConsole.WriteLine();
        }
    }
}
