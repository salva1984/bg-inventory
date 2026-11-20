using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BgInventario.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RegisteredByUserId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileContent = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceReceipts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceReceipts_Users_RegisteredByUserId",
                        column: x => x.RegisteredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceReceiptId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ProductCreated = table.Column<bool>(type: "bit", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceReceiptLines_InvoiceReceipts_InvoiceReceiptId",
                        column: x => x.InvoiceReceiptId,
                        principalTable: "InvoiceReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceReceiptLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceiptLines_InvoiceReceiptId",
                table: "InvoiceReceiptLines",
                column: "InvoiceReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceiptLines_ProductId",
                table: "InvoiceReceiptLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceipts_CreatedAt",
                table: "InvoiceReceipts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceipts_RegisteredByUserId",
                table: "InvoiceReceipts",
                column: "RegisteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceipts_SupplierId",
                table: "InvoiceReceipts",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceReceiptLines");

            migrationBuilder.DropTable(
                name: "InvoiceReceipts");
        }
    }
}
