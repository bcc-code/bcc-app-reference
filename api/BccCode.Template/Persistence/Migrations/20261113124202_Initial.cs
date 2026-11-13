using System;
using BccCode.Template.Changes;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BccCode.Template.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_uid = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    protected_ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                    issuer = table.Column<string>(type: "text", nullable: true),
                    sid = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_sessions", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    friendly_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.friendly_name);
                });

            migrationBuilder.CreateTable(
                name: "change",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_change_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "actor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "note",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    change_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note", x => x.id);
                    table.ForeignKey(
                        name: "fk_note_change_change_id",
                        column: x => x.change_id,
                        principalTable: "change",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actor_person_uid",
                table: "actor",
                column: "person_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_issuer_sid",
                table: "auth_sessions",
                columns: new[] { "issuer", "sid" });

            migrationBuilder.CreateIndex(
                name: "ix_change_actor_id",
                table: "change",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_change_subject_id_position",
                table: "change",
                columns: new[] { "subject_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_note_change_id",
                table: "note",
                column: "change_id");

            migrationBuilder.CreateIndex(
                name: "ix_note_owner_id_created_at",
                table: "note",
                columns: new[] { "owner_id", "created_at" },
                filter: "removed_at is null");

            // History (ADR 0028): the two functions, then the twin of every audited table.
            migrationBuilder.Sql(HistorySql.CreateTrackFunction);
            migrationBuilder.Sql(HistorySql.CreateSyncFunction);
            migrationBuilder.Sql(HistorySql.Sync("note"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(HistorySql.DropHistory("note"));
            migrationBuilder.DropTable(
                name: "auth_sessions");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "note");

            migrationBuilder.DropTable(
                name: "change");

            migrationBuilder.DropTable(
                name: "actor");

            migrationBuilder.Sql(HistorySql.DropFunctions);
        }
    }
}
