using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniSift.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPgTrgmExtensionAndIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable pg_trgm extension for trigram-based keyword similarity search.
            // Used by the hybrid vector+keyword search arm in VectorSearchPlugin.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // GIN trigram index on document_chunks.content for fast word_similarity queries.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_document_chunks_content_trgm " +
                "ON document_chunks USING GIN (content gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_document_chunks_content_trgm;");

            // Note: we do NOT drop the pg_trgm extension on Down — it may be in use
            // by other objects and dropping extensions is irreversible/destructive.
        }
    }
}
