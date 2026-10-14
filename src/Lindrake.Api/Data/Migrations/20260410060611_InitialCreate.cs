using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OmniSift.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_sources", x => x.id);
                    table.ForeignKey(
                        name: "FK_data_sources_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "query_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    query_text = table.Column<string>(type: "text", nullable: false),
                    response_text = table.Column<string>(type: "text", nullable: true),
                    plugins_used = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    sources = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_query_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_query_history_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    embedding = table.Column<Vector>(type: "vector(384)", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_chunks_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_chunks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_sources_tenant_id",
                table: "data_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_data_sources_tenant_id_status",
                table: "data_sources",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_data_source_id",
                table: "document_chunks",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_tenant_id",
                table: "document_chunks",
                column: "tenant_id");

            migrationBuilder.Sql("""
                CREATE INDEX "IX_document_chunks_embedding_hnsw"
                ON document_chunks
                USING hnsw (embedding vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_query_history_tenant_id",
                table: "query_history",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_query_history_tenant_id_created_at",
                table: "query_history",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_tenants_api_key_hash",
                table: "tenants",
                column: "api_key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE data_sources ENABLE ROW LEVEL SECURITY;
                ALTER TABLE data_sources FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_data_sources ON data_sources
                    FOR ALL
                    USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant', true)::uuid);

                ALTER TABLE document_chunks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE document_chunks FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_document_chunks ON document_chunks
                    FOR ALL
                    USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant', true)::uuid);

                ALTER TABLE query_history ENABLE ROW LEVEL SECURITY;
                ALTER TABLE query_history FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_query_history ON query_history
                    FOR ALL
                    USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant', true)::uuid);
                """);

            migrationBuilder.InsertData(
                table: "tenants",
                columns: ["id", "name", "slug", "api_key_hash"],
                values: [new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Development Tenant", "dev", "a826b73fb90c5c344dba0f626bca96931c9c0c37f790aa644592f7d667cbf032"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation_query_history ON query_history;
                DROP POLICY IF EXISTS tenant_isolation_document_chunks ON document_chunks;
                DROP POLICY IF EXISTS tenant_isolation_data_sources ON data_sources;

                DROP INDEX IF EXISTS "IX_document_chunks_embedding_hnsw";
                """);

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "query_history");

            migrationBuilder.DropTable(
                name: "data_sources");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
