using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BgInventario.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductTrigrams",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Trigram = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTrigrams", x => new { x.Trigram, x.ProductId });
                    table.ForeignKey(
                        name: "FK_ProductTrigrams_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierProductAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedDescription = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierProductAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierProductAliases_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierProductAliases_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTrigrams_ProductId",
                table: "ProductTrigrams",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductAliases_ProductId",
                table: "SupplierProductAliases",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductAliases_SupplierId_NormalizedDescription",
                table: "SupplierProductAliases",
                columns: new[] { "SupplierId", "NormalizedDescription" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductAliases_SupplierId_SupplierItemCode",
                table: "SupplierProductAliases",
                columns: new[] { "SupplierId", "SupplierItemCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductTrigrams");

            migrationBuilder.DropTable(
                name: "SupplierProductAliases");
        }
    }
}
