# Changelog

All notable changes to **RUNIX Database Studio** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.1.0] - 2026-11-25

### 🚀 Added
- **Oracle Database Enterprise Edition Support**:
  - Full native cross-platform support for **Oracle Database (12c, 19c, 21c, 23ai)** powered by 100% managed driver (`Oracle.ManagedDataAccess.Core` v23.26.401), eliminating native C Oracle Instant Client dependencies.
  - Dedicated **Vard** resilience integration with *Exponential Backoff Retry* and *Circuit Breaker* policies targeting transient Oracle network and TNS failure codes.
  - Modern connection configuration: official Oracle vector brand styling, default port `1521`, dynamic toggling between **Service Name**, **SID**, and raw **TNS Descriptors**, and administrative privilege selection (`NORMAL`, `SYSDBA`, `SYSOPER`).
  - Deep schema introspection querying Oracle data dictionary views (`ALL_TABLES`, `ALL_VIEWS`, `ALL_TAB_COLUMNS`, `ALL_CONSTRAINTS`, `ALL_IND_COLUMNS`).
  - Full type mapping for Oracle native types: `NUMBER(p, s)`, `VARCHAR2`, `NVARCHAR2`, `DATE`, `TIMESTAMP`, `CLOB`, `BLOB`, `RAW`, and `ROWID`.
  - Comprehensive PL/SQL object support: navigation and DDL generation for **Packages** (unified Specification and Body), **Stored Procedures**, **Functions**, **Triggers**, and **Sequences**.
  - Modern ANSI SQL:2008 pagination (`OFFSET :offset ROWS FETCH NEXT :limit ROWS ONLY`) with deterministic ordering fallback.
  - Complete integration across RUNIX tools:
    - **Visual Query Builder**: uppercase quoted identifiers (`"COLUMN"`), filters, and auto-joins based on Oracle Foreign Key constraints.
    - **Interactive DER Canvas**: visual modeling with Oracle DDL generator (automatically omitting unsupported `ON UPDATE` foreign key clauses).
    - **Mock Data Generator**: Oracle parent FK sampling with `FETCH NEXT 1000 ROWS ONLY`, individual `INSERT INTO` statements, boolean `1`/`0` mapping, and `TO_DATE` literal formatting.
- **Oracle Database Health & Telemetry Dashboard**:
  - Storage telemetry: granular **Tablespaces** inspection (`DBA_TABLESPACES`, `DBA_DATA_FILES`, `DBA_FREE_SPACE` with non-DBA fallback to `USER_TABLESPACES`/`USER_SEGMENTS`).
  - Cache performance metrics: real-time **Buffer Cache Hit Ratio** via `V$SYSSTAT`.
  - Active session monitoring and blocking lock detection via `V$SESSION`.
  - Assisted Oracle maintenance: `ALTER TABLE SHRINK SPACE COMPACT`, `ANALYZE TABLE COMPUTE STATISTICS`, `ALTER INDEX REBUILD`, and structural integrity audits (disabled constraints and invalid schema objects).
- **Expanded Object Support for MySQL & SQL Server**:
  - Integrated support for Stored Procedures, Functions, and Triggers in **MySQL** (`information_schema.routines`, `information_schema.triggers`) and **SQL Server** (`sys.objects`, `sys.triggers`) with on-demand DDL resolution.

### ⚡ Performance
- **Eliminated N+1 Query Bottleneck in PostgreSQL Introspection**:
  - Replaced legacy `information_schema.tables` + loop of individual row count queries with a single atomic query against native `pg_class` and `pg_namespace` catalogs, reducing schema load times from several seconds to under 10ms.
- **Lazy DDL Loading for Programmable Objects**:
  - Streamlined boot catalog queries across PostgreSQL, MySQL, and SQL Server to fetch lightweight metadata only (name, schema, return type, arguments), deferring heavy DDL decompilation (`pg_get_functiondef`, `pg_get_triggerdef`, `SHOW CREATE`) to on-demand user requests.
- **Atomic Row Count Estimates Across All Database Engines**:
  - MySQL/MariaDB: extracted pre-computed row counts directly from `information_schema.tables` (`TABLE_ROWS`).
  - SQL Server: grouped partition row counts from `sys.partitions` (`SUM(p.rows)`) in the primary table listing query.
  - SQLite: utilized `sqlite_stat1` pre-computed statistics to avoid full table scans on large databases.
- **Progressive Sidebar Rendering (`WorkspaceView`)**:
  - The object tree now displays tables immediately upon connection while programmable routines and local storage cache load asynchronously in the background, eliminating UI freezes.

### 🧪 Testing
- Expanded automated test suite from 487 to **532 passing unit and integration tests** (322 in `Runix.Core.Tests` + 210 in `Runix.Infrastructure.Tests`) with 100% pass rate.

---

## [2.0.0] - 2026-11-24

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
