# Changelog

All notable changes to **RUNIX Database Studio** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.6.0] - 2026-11-24

### 🚀 Added
- **Multi-Engine Relational Support**:
  - Full native support for **MySQL** and **MariaDB** via `MySqlConnector`.
  - Full native support for **Microsoft SQL Server (MSSQL / Azure SQL)** via `Microsoft.Data.SqlClient`.
  - Unified modern dropdown selector (`ComboBox`) with official vector brand logos (PostgreSQL Slonik, SQLite quill, MySQL Sakila, MariaDB sea lion, and SQL Server cylinder).
  - Dynamic port auto-assignment (`5432`, `3306`, `1433`) and dialect-specific options (SSL modes, Trust Server Certificate).
- **Mock Data Generator**:
  - Smart schema-aware test data synthesis tailored for relational database tables.
  - Foreign key sampling: collects valid parent IDs from related tables to preserve strict `FOREIGN KEY` referential integrity.
  - Configurable batch quantities (10, 25, **50 default**, 100, 250, 500, 1,000 rows) with nullable percentage sliders.
  - Real-time 5-row preview grid with instant regeneration.
  - Transactional direct database seeding or export to atomic `INSERT INTO` SQL scripts.
- **Database Object Inspector & Editor**:
  - Dedicated inspector and DDL generator for non-table schema objects: **Views**, **Triggers**, **Indexes**, and **Stored Procedures / Functions**.
- **Enhanced Entity-Relationship Diagram (ERD)**:
  - Multi-format vector visual export: **PDF documents** (Fit to Diagram, A4 Landscape, A3 Landscape), Retina 2x **PNG**, and full **SQL DDL** scripts.
  - Real-time entity search filter (`Ctrl+F` on ERD canvas).
- **Hierarchical Connection Management**:
  - Collapsible category folders in sidebar with drag-and-drop reordering.
  - Inline category bar for quick creation, renaming, and deletion.
  - Environment badges: Production (`🚨 PROD`), Read-Only (`🔒 RO`), and SSH Tunnel (`🔐 SSH`).

### ⚡ Changed
- Upgraded test suite to **487 automated unit and integration tests** (305 in `Runix.Core.Tests` + 182 in `Runix.Infrastructure.Tests`) with 100% pass rate.
- Extended `ResilientDatabaseService` with Vard retry and circuit breaker policies across all new network database engines.
- Refactored dropdown engine selector to typed `DatabaseEngineOption` data templates, ensuring edge-to-edge alignment and sharp vector rendering.

### 🐛 Fixed
- Fixed ComboBox layout where the selected engine item was centered with an offset gap instead of stretching properly.
- Fixed default row count selection in Mock Data Generator dialog to ensure **50** is selected upon opening.

---

## [1.1.0] - 2026-11-15

### 🚀 Added
- **Interactive Visual ERD**:
  - Bidirectional visual modeling canvas with Bezier curve relationships and cardinality markers.
  - Safe DDL migration preview with atomic transaction execution and rollback on failure.
- **In-Grid Data Editing**:
  - In-place cell edits with visual dirty tracking (amber for modifications, green for insertions, red for deletions).
  - Stage and atomic commit bar with SQL Injection protection.
  - Record Inspector side drawer with JSON Pretty-Print/Minify and Hex Dump for binary BLOBs.
- **Productivity & DBA Suite**:
  - Global Command Palette (`Ctrl/Cmd+K`) with fuzzy search and acronym matching.
  - Full-database cross-table data search.
  - Visual Explain Plan & Profiler with cost heatmaps and index recommendation heuristics.
  - Analytical SkiaSharp Quick Charts (Vertical/Horizontal Bar, Line/Area, Scatter, Donut/Pie).
  - PostgreSQL Administration Suite (Roles, Privilege Matrix, Active Sessions).
  - 3-step CSV/Excel Import Wizard and multi-format streaming Exporter (CSV, JSON, XLSX, SQL).

---

## [1.0.0] - 2026-11-14

### 🚀 Added
- Initial public release of **RUNIX Database Studio** for macOS (Apple Silicon & Intel), Windows (x64), and Linux (x64).
- Native .NET 10 & Avalonia UI 11 architecture with zero Electron overhead.
- Native drivers for **PostgreSQL** (`Npgsql`) and **SQLite** (`Microsoft.Data.Sqlite`).
- Transient fault tolerance with **Vard** resilience library (exponential backoff & circuit breaker).
- AES-256-GCM credential vault and `.rcf` encrypted bundle export/import.
- Built-in SSH port forwarding over ephemeral local ports.
- Contextual SQL IntelliSense autocomplete and in-process SQL Beautifier (`Ctrl+Shift+F`).
- Headless Skia rendering engine for automated screenshot verification.
- Complete bilingual support (English `en-US` and Portuguese `pt-BR`).
