using BaGetter.Database.PostgreSql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGetter.Database.PostgreSql.Migrations;

[DbContext(typeof(PostgreSqlContext))]
[Migration("20261015150000_AddPackageStorageRevision")]
public partial class AddPackageStorageRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StorageRevision",
            table: "Packages",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StorageRevision", table: "Packages");
    }
}
