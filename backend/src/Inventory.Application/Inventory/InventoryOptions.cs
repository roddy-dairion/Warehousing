namespace Inventory.Application.Inventory;

public sealed class InventoryOptions
{
    public const string Section = "Inventory";

    /// <summary>The person who submits an adjustment cannot approve it (segregation of duties). Default on.</summary>
    public bool RequireDifferentApprover { get; set; } = true;

    /// <summary>Lots expiring within this many days are shown as alerts.</summary>
    public int ExpiryAlertDays { get; set; } = 30;

    /// <summary>
    /// Minimum remaining shelf life, in days, for a lot to be reserved for a dispatch (Q20). 0 = anything not yet expired.
    /// </summary>
    public int MinShelfLifeDays { get; set; }

    /// <summary>How long stored idempotency responses are kept.</summary>
    public int IdempotencyRetentionHours { get; set; } = 48;

    /// <summary>Run the background maintenance job (idempotency cleanup and the reconciliation check).</summary>
    public bool MaintenanceEnabled { get; set; } = true;

    /// <summary>Hours between reconciliation checks by the maintenance job.</summary>
    public int ReconciliationIntervalHours { get; set; } = 24;
}
