using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BccCode.Template.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthSessionSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "issuer",
                table: "auth_sessions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subject",
                table: "auth_sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_issuer_subject",
                table: "auth_sessions",
                columns: new[] { "issuer", "subject" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_auth_sessions_issuer_subject",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "subject",
                table: "auth_sessions");

            migrationBuilder.AlterColumn<string>(
                name: "issuer",
                table: "auth_sessions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
