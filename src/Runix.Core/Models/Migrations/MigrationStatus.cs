namespace Runix.Core.Models.Migrations;

public enum MigrationStatus
{
    Pending,
    Applied,
    MismatchChecksum,
    Failed
}
