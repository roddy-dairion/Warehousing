namespace Inventory.Domain.Common;

/// <summary>Base class for entities with a time-ordered UUID (v7) primary key.</summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}
