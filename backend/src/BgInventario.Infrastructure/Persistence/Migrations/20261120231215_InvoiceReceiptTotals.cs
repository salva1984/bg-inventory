using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BgInventario.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceReceiptTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceReceipts_SupplierId",
                table: "InvoiceReceipts");

            // Edited by hand: EF scaffolded a drop + add, which would lose the recorded amounts.
            // "Total" always was the sum of the lines before taxes, so it is renamed and widened.
            migrationBuilder.RenameColumn(
                name: "Total",
                table: "InvoiceReceipts",
                newName: "Subtotal");

            migrationBuilder.AlterColumn<decimal>(
                name: "Subtotal",
                table: "InvoiceReceipts",
                type: "decimal(21,2)",
                precision: 21,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "InvoiceTotal",
                table: "InvoiceReceipts",
                type: "decimal(21,2)",
                precision: 21,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceipts_SupplierId_InvoiceNumber",
                table: "InvoiceReceipts",
                columns: new[] { "SupplierId", "InvoiceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceReceipts_SupplierId_InvoiceNumber",
                table: "InvoiceReceipts");

            migrationBuilder.DropColumn(
                name: "InvoiceTotal",
                table: "InvoiceReceipts");

            migrationBuilder.AlterColumn<decimal>(
                name: "Subtotal",
                table: "InvoiceReceipts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(21,2)",
                oldPrecision: 21,
                oldScale: 2);

            migrationBuilder.RenameColumn(
                name: "Subtotal",
                table: "InvoiceReceipts",
                newName: "Total");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceipts_SupplierId",
                table: "InvoiceReceipts",
                column: "SupplierId");
        }
    }
}
