using BaGetter.Database.MySql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.MySql.Migrations;

[DbContext(typeof(MySqlContext))]
[Migration("20261015150000_AddPackageStorageRevision")]
public partial class AddPackageStorageRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StorageRevision",
            table: "Packages",
            type: "varchar(32)",
            maxLength: 32,
            nullable: true);
        // Storage identifiers contain ASCII hex digits only.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StorageRevision", table: "Packages");
    }
}
