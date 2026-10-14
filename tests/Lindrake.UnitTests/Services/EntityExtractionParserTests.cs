// ============================================================
// Unit Tests — EntityExtractionService.ParseGraphJson
// Verifies JSON parsing, fence stripping, prose extraction, and error handling
// ============================================================

using FluentAssertions;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class EntityExtractionParserTests
{
    [Fact]
    public void ParseGraphJson_WellFormedJson_ReturnsGraph()
    {
        const string raw = """
            {
              "nodes": [
                {"id": "node-1", "label": "Alice", "type": "person", "mentions": 5},
                {"id": "node-2", "label": "Acme Corp", "type": "org", "mentions": 3}
              ],
              "edges": [
                {"source": "node-1", "target": "node-2", "relationship": "works at"}
              ],
              "timeline": [
                {"date": "2023-01-15", "label": "Alice joins Acme", "entities": ["node-1", "node-2"]}
              ]
            }
            """;

        var result = EntityExtractionService.ParseGraphJson(raw);

        result.Nodes.Should().HaveCount(2);
        result.Nodes[0].Id.Should().Be("node-1");
        result.Nodes[0].Label.Should().Be("Alice");
        result.Nodes[0].Type.Should().Be("person");
        result.Nodes[0].Mentions.Should().Be(5);

        result.Edges.Should().HaveCount(1);
        result.Edges[0].Source.Should().Be("node-1");
        result.Edges[0].Target.Should().Be("node-2");
        result.Edges[0].Relationship.Should().Be("works at");

        result.Timeline.Should().HaveCount(1);
        result.Timeline[0].Date.Should().Be("2023-01-15");
        result.Timeline[0].Entities.Should().ContainInOrder("node-1", "node-2");
    }

    [Fact]
    public void ParseGraphJson_JsonInCodeFences_StripsAndParses()
    {
        const string raw = """
            ```json
            {
              "nodes": [{"id": "node-1", "label": "Bob", "type": "person", "mentions": 2}],
              "edges": [],
              "timeline": []
            }
            ```
            """;

        var result = EntityExtractionService.ParseGraphJson(raw);

        result.Nodes.Should().HaveCount(1);
        result.Nodes[0].Label.Should().Be("Bob");
        result.Edges.Should().BeEmpty();
        result.Timeline.Should().BeEmpty();
    }

    [Fact]
    public void ParseGraphJson_JsonWithSurroundingProse_ExtractsObject()
    {
        const string raw = """
            Here is the knowledge graph you requested:
            {"nodes": [{"id": "node-1", "label": "London", "type": "place", "mentions": 4}], "edges": [], "timeline": []}
            Hope that helps!
            """;

        var result = EntityExtractionService.ParseGraphJson(raw);

        result.Nodes.Should().HaveCount(1);
        result.Nodes[0].Label.Should().Be("London");
        result.Nodes[0].Type.Should().Be("place");
    }

    [Fact]
    public void ParseGraphJson_MissingFields_ReturnsEmptyLists()
    {
        const string raw = """{"nodes": null}""";

        var result = EntityExtractionService.ParseGraphJson(raw);

        result.Should().NotBeNull();
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Timeline.Should().BeEmpty();
    }

    [Fact]
    public void ParseGraphJson_InvalidJson_ReturnsEmpty()
    {
        const string raw = "not json at all";

        var result = EntityExtractionService.ParseGraphJson(raw);

        result.Should().NotBeNull();
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Timeline.Should().BeEmpty();
    }

    [Fact]
    public void ParseGraphJson_TimelineIsOrdered_SortedAscending()
    {
        const string raw = """
            {
              "nodes": [],
              "edges": [],
              "timeline": [
                {"date": "2023-06-01", "label": "Event C", "entities": []},
                {"date": "2021-03-15", "label": "Event A", "entities": []},
                {"date": "2022-11-30", "label": "Event B", "entities": []}
              ]
            }
            """;

        var result = EntityExtractionService.ParseGraphJson(raw);

        result.Timeline.Should().HaveCount(3);
        result.Timeline[0].Date.Should().Be("2021-03-15");
        result.Timeline[1].Date.Should().Be("2022-11-30");
        result.Timeline[2].Date.Should().Be("2023-06-01");
    }
}
