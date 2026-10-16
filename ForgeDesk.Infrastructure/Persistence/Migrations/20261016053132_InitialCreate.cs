using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    NormalizedRootPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ProjectType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastScannedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastOpenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Scans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FilesDiscovered = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesProcessed = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ToolVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    QueuedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CancellationRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    FindingCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scans_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DiscoveryStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NormalizedPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    LineCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceFiles_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Symbols",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SymbolKey = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: true),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Symbols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Symbols_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Symbols_SourceFiles_SourceFileId",
                        column: x => x.SourceFileId,
                        principalTable: "SourceFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceSymbolId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetSymbolId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelationshipType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dependencies_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Dependencies_Symbols_SourceSymbolId",
                        column: x => x.SourceSymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dependencies_Symbols_TargetSymbolId",
                        column: x => x.TargetSymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SourceFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SymbolId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: true),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Findings_Scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "Scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Findings_SourceFiles_SourceFileId",
                        column: x => x.SourceFileId,
                        principalTable: "SourceFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Findings_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_ScanId_SourceSymbolId_TargetSymbolId_RelationshipType",
                table: "Dependencies",
                columns: new[] { "ScanId", "SourceSymbolId", "TargetSymbolId", "RelationshipType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceSymbolId",
                table: "Dependencies",
                column: "SourceSymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_TargetSymbolId",
                table: "Dependencies",
                column: "TargetSymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_ScanId_Severity",
                table: "Findings",
                columns: new[] { "ScanId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_SourceFileId",
                table: "Findings",
                column: "SourceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_SymbolId",
                table: "Findings",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsFavorite_LastOpenedAtUtc",
                table: "Projects",
                columns: new[] { "IsFavorite", "LastOpenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_NormalizedRootPath",
                table: "Projects",
                column: "NormalizedRootPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Scans_ProjectId_QueuedAtUtc",
                table: "Scans",
                columns: new[] { "ProjectId", "QueuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceFiles_ScanId_Extension",
                table: "SourceFiles",
                columns: new[] { "ScanId", "Extension" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceFiles_ScanId_RelativePath",
                table: "SourceFiles",
                columns: new[] { "ScanId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_ScanId_SymbolKey",
                table: "Symbols",
                columns: new[] { "ScanId", "SymbolKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_SourceFileId",
                table: "Symbols",
                column: "SourceFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dependencies");

            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Symbols");

            migrationBuilder.DropTable(
                name: "SourceFiles");

            migrationBuilder.DropTable(
                name: "Scans");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
