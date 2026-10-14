// ============================================================
// OmniSift.Shared — JSON Source Generation Context
// Compile-time serialization for all API DTOs
// ============================================================

using System.Text.Json.Serialization;
using OmniSift.Shared.DTOs;

namespace OmniSift.Shared;

/// <summary>
/// Source-generated JsonSerializerContext for all shared DTOs.
/// Opt-in to compile-time JSON serialization, eliminating runtime
/// reflection and enabling Native AOT compatibility.
/// </summary>
[JsonSerializable(typeof(AgentQueryRequest))]
[JsonSerializable(typeof(AgentQueryResponse))]
[JsonSerializable(typeof(AgentStreamDeltaEvent))]
[JsonSerializable(typeof(AgentStreamFinalEvent))]
[JsonSerializable(typeof(AgentStreamErrorEvent))]
[JsonSerializable(typeof(ConversationMessage))]
[JsonSerializable(typeof(SourceCitation))]
[JsonSerializable(typeof(DataSourceDto))]
[JsonSerializable(typeof(List<DataSourceDto>))]
[JsonSerializable(typeof(WebIngestionRequest))]
[JsonSerializable(typeof(IngestionResponse))]
[JsonSerializable(typeof(List<ConversationMessage>))]
[JsonSerializable(typeof(List<SourceCitation>))]
[JsonSerializable(typeof(GenerateReportRequest))]
[JsonSerializable(typeof(GenerateReportResponse))]
[JsonSerializable(typeof(ReportTurn))]
[JsonSerializable(typeof(List<ReportTurn>))]
[JsonSerializable(typeof(AuditLogDto))]
[JsonSerializable(typeof(List<AuditLogDto>))]
[JsonSerializable(typeof(TenantExportDto))]
[JsonSerializable(typeof(QueryHistoryDto))]
[JsonSerializable(typeof(List<QueryHistoryDto>))]
[JsonSerializable(typeof(DeleteAccountDataRequest))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(EntityNode))]
[JsonSerializable(typeof(EntityEdge))]
[JsonSerializable(typeof(TimelineEntry))]
[JsonSerializable(typeof(EntityGraphResponse))]
[JsonSerializable(typeof(List<EntityNode>))]
[JsonSerializable(typeof(List<EntityEdge>))]
[JsonSerializable(typeof(List<TimelineEntry>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class OmniSiftJsonContext : JsonSerializerContext { }
