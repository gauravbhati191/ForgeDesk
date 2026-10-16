using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeDesk.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAnalysisState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AnalysisCompletedAtUtc",
            table: "Scans",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AnalysisDependencyCount",
            table: "Scans",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "AnalysisErrorMessage",
            table: "Scans",
            type: "TEXT",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AnalysisFilesProcessed",
            table: "Scans",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AnalysisStartedAtUtc",
            table: "Scans",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AnalysisSymbolCount",
            table: "Scans",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "AnalysisStatus",
            table: "Scans",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "NotStarted");

        migrationBuilder.CreateIndex(
            name: "IX_Dependencies_ScanId_TargetSymbolId",
            table: "Dependencies",
            columns: new[] { "ScanId", "TargetSymbolId" });

        migrationBuilder.CreateIndex(
            name: "IX_Scans_ProjectId_AnalysisStatus",
            table: "Scans",
            columns: new[] { "ProjectId", "AnalysisStatus" });

        migrationBuilder.CreateIndex(
            name: "IX_Symbols_ScanId_SourceFileId_Kind",
            table: "Symbols",
            columns: new[] { "ScanId", "SourceFileId", "Kind" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Dependencies_ScanId_TargetSymbolId",
            table: "Dependencies");

        migrationBuilder.DropIndex(
            name: "IX_Scans_ProjectId_AnalysisStatus",
            table: "Scans");

        migrationBuilder.DropIndex(
            name: "IX_Symbols_ScanId_SourceFileId_Kind",
            table: "Symbols");

        migrationBuilder.DropColumn(
            name: "AnalysisCompletedAtUtc",
            table: "Scans");

        migrationBuilder.DropColumn(
            name: "AnalysisDependencyCount",
            table: "Scans");

        migrationBuilder.DropColumn(
            name: "AnalysisErrorMessage",
            table: "Scans");

        migrationBuilder.DropColumn(
            name: "AnalysisFilesProcessed",
            table: "Scans");

        migrationBuilder.DropColumn(
            name: "AnalysisStartedAtUtc",
            table: "Scans");

        migrationBuilder.DropColumn(
            name: "AnalysisSymbolCount",
            table: "Scans");

        migrationBuilder.DropColumn(
            name: "AnalysisStatus",
            table: "Scans");
    }
}
