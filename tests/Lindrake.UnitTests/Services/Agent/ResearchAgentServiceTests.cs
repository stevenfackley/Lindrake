// ============================================================
// Unit Tests — ResearchAgentService
// ============================================================

using System.ComponentModel;
using FluentAssertions;
using Lindrake.Api.Infrastructure;
using Lindrake.Api.Middleware;
using Lindrake.Api.Options;
using Lindrake.Api.Services;
using Lindrake.Api.Services.Agent;
using Lindrake.Shared.DTOs;
using Lindrake.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lindrake.UnitTests.Services.Agent;

public sealed class ResearchAgentServiceTests
{
    private readonly string _databaseName = "agent-" + Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly InvocationTracker _tracker = new();
    private readonly CitationAccumulator _citations = new();

    private static readonly AgentQueryRequest Request = new() { Query = "Where was my grandfather born?" };

    [Fact]
    public async Task RunAsync_ModelCallsAPlugin_ReportsThePluginAsUsed()
    {
        // The real OpenAI connector runs the tool call; the old code looked for a
        // metadata key on tool messages that SK never sets, so this was always empty.
        var handler = new ScriptedOpenAiHandler(
            ScriptedOpenAiHandler.ToolCall("VectorSearch-SearchDocuments", new { query = "grandfather" }),
            ScriptedOpenAiHandler.Text("He was born in Cork."));
        var builder = ScriptedOpenAiHandler.KernelBuilder(handler);
        var plugin = new StubSearchPlugin();
        builder.Plugins.AddFromObject(plugin, "VectorSearch");
        var kernel = builder.Build();
        kernel.AutoFunctionInvocationFilters.Add(new PluginInvocationTrackingFilter(_tracker));

        var result = await CreateService(kernel).RunAsync(Request, CancellationToken.None);

        plugin.Queries.Should().Equal("grandfather");
        result.Response.Should().Be("He was born in Cork.");
        result.PluginsUsed.Should().Equal("VectorSearch");

        await using var verify = InMemoryDb.ForTenant(_databaseName, _tenantId);
        var history = await verify.QueryHistories.SingleAsync();
        history.PluginsUsed.Should().Equal("VectorSearch");
    }

    [Fact]
    public async Task RunAsync_RecordsQueryHistoryAndAuditEntry()
    {
        var kernel = KernelWith(new ScriptedChatCompletionService("Hello ", "world"));

        var result = await CreateService(kernel).RunAsync(Request, CancellationToken.None);

        result.Response.Should().Be("Hello world");
        await using var verify = InMemoryDb.ForTenant(_databaseName, _tenantId);
        var history = await verify.QueryHistories.SingleAsync();
        history.TenantId.Should().Be(_tenantId);
        history.QueryText.Should().Be(Request.Query);
        history.ResponseText.Should().Be("Hello world");
        var audit = await verify.AuditLogs.SingleAsync();
        audit.Action.Should().Be("agent_query");
        audit.ResourceId.Should().Be(history.Id);
    }

    [Fact]
    public async Task RunAsync_PersistsTheCitationsInQueryHistory()
    {
        _citations.AddWebResult("https://example.test/cork", "Cork records", 0.9);
        var kernel = KernelWith(new ScriptedChatCompletionService("Cork."));

        var result = await CreateService(kernel).RunAsync(Request, CancellationToken.None);

        result.Sources.Should().ContainSingle();
        await using var verify = InMemoryDb.ForTenant(_databaseName, _tenantId);
        var history = await verify.QueryHistories.SingleAsync();
        history.Sources.Should().ContainSingle().Which.Should().BeEquivalentTo(result.Sources[0]);
        history.Sources[0].Url.Should().Be("https://example.test/cork");
    }

    [Fact]
    public async Task StreamAsync_YieldsDeltasThenCompletedWithSources()
    {
        _citations.AddWebResult("https://example.test", "Example", 0.9);
        var kernel = KernelWith(new ScriptedChatCompletionService("Hello ", "", "world"));

        var events = await CollectAsync(CreateService(kernel).StreamAsync(Request, CancellationToken.None));

        events.Should().HaveCount(3);
        events[0].Should().Be(new AgentStreamEvent.Delta("Hello "));
        events[1].Should().Be(new AgentStreamEvent.Delta("world"));
        var completed = events[2].Should().BeOfType<AgentStreamEvent.Completed>().Subject;
        completed.Result.Response.Should().Be("Hello world");
        completed.Result.Sources.Should().ContainSingle().Which.Url.Should().Be("https://example.test");
    }

    [Fact]
    public async Task StreamAsync_RequestCancelledAfterLastChunk_StillPersistsHistory()
    {
        // Client disconnects as the answer finishes: the request token is cancelled
        // before history is written. It used to cancel SaveChangesAsync too.
        using var requestAborted = new CancellationTokenSource();
        var chat = new ScriptedChatCompletionService("Done.") { OnStreamCompleted = requestAborted.Cancel };

        var events = await CollectAsync(CreateService(KernelWith(chat)).StreamAsync(Request, requestAborted.Token));

        events.Last().Should().BeOfType<AgentStreamEvent.Completed>();
        await using var verify = InMemoryDb.ForTenant(_databaseName, _tenantId);
        (await verify.QueryHistories.SingleAsync()).ResponseText.Should().Be("Done.");
        (await verify.AuditLogs.SingleAsync()).Action.Should().Be("agent_query_stream");
    }

    [Fact]
    public async Task StreamAsync_EmptyAnswer_RecordsFallbackText()
    {
        var events = await CollectAsync(
            CreateService(KernelWith(new ScriptedChatCompletionService())).StreamAsync(Request, CancellationToken.None));

        events.Should().ContainSingle().Which.Should().BeOfType<AgentStreamEvent.Completed>()
            .Which.Result.Response.Should().Be(ResearchAgentService.NoResponseText);
    }

    [Fact]
    public void StreamAsync_WithoutLlm_ThrowsBeforeEnumeration()
    {
        var service = CreateService(Kernel.CreateBuilder().Build());

        var act = () => service.StreamAsync(Request, CancellationToken.None);

        act.Should().Throw<LlmNotConfiguredException>();
    }

    [Fact]
    public async Task RunAsync_UsesServerSideHistoryCap()
    {
        var chat = new ScriptedChatCompletionService("ok");
        var request = Request with
        {
            ConversationHistory = [.. Enumerable.Range(0, 30).Select(i =>
                new ConversationMessage { Role = i % 2 == 0 ? "user" : "assistant", Content = $"m{i}" })]
        };

        await CreateService(KernelWith(chat), maxHistoryMessages: 4).RunAsync(request, CancellationToken.None);

        chat.LastHistory!.Select(m => m.Content).Should().Equal(
            ResearchProfiles.FamilyHistory.SystemPrompt, "m26", "m27", "m28", "m29", request.Query);
    }

    private ResearchAgentService CreateService(Kernel kernel, int maxHistoryMessages = 20)
    {
        var tenant = new TenantContext();
        tenant.Set(_tenantId);
        var db = InMemoryDb.Create(_databaseName, tenant);
        var audit = new AuditLogger(db, tenant, new HttpContextAccessor());

        return new ResearchAgentService(
            kernel,
            new ChatHistoryFactory(
                Microsoft.Extensions.Options.Options.Create(new AgentOptions { MaxHistoryMessages = maxHistoryMessages }),
                Microsoft.Extensions.Options.Options.Create(new ResearchProfileOptions())),
            _tracker,
            _citations,
            new QueryHistoryRecorder(db, tenant, audit),
            tenant,
            NullLogger<ResearchAgentService>.Instance);
    }

    private static Kernel KernelWith(IChatCompletionService chat)
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(chat);
        return builder.Build();
    }

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var list = new List<AgentStreamEvent>();
        await foreach (var evt in events)
            list.Add(evt);
        return list;
    }

    private sealed class StubSearchPlugin
    {
        public List<string> Queries { get; } = [];

        [KernelFunction("SearchDocuments")]
        [Description("Searches documents.")]
        public string SearchDocuments(string query)
        {
            Queries.Add(query);
            return "Cork, 1921";
        }
    }
}
