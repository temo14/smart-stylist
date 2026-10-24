using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartStylist.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsletterPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "catalog",
                table: "price_snapshots",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Crawl");

            migrationBuilder.CreateTable(
                name: "newsletter_emails",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    from = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    shop_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sale_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    promo_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    products_found = table.Column<int>(type: "integer", nullable: false),
                    products_rejected = table.Column<int>(type: "integer", nullable: false),
                    prices_written = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_newsletter_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_newsletter_emails_shops_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "catalog",
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_newsletter_emails_message_id",
                schema: "catalog",
                table: "newsletter_emails",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_newsletter_emails_received_at",
                schema: "catalog",
                table: "newsletter_emails",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_newsletter_emails_shop_id",
                schema: "catalog",
                table: "newsletter_emails",
                column: "shop_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "newsletter_emails",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "catalog",
                table: "price_snapshots");
        }
    }
}
