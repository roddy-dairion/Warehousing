using Inventory.Domain.Customers;
using Inventory.Domain.Dispatches;
using Inventory.Domain.Documents;
using Inventory.Domain.Files;
using Inventory.Domain.Inventory;
using Inventory.Domain.Products;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal sealed class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> b)
    {
        b.ToTable("dispatches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.ShortDecision).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.CustomerReference).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Carrier).HasMaxLength(100);
        b.Property(x => x.TrackingNumber).HasMaxLength(100);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.OwnsOne(x => x.ShipTo, a =>
        {
            a.Property(p => p.Label).HasColumnName("ship_to_label").HasMaxLength(60);
            a.Property(p => p.Line1).HasColumnName("ship_to_line1").HasMaxLength(200);
            a.Property(p => p.Line2).HasColumnName("ship_to_line2").HasMaxLength(200);
            a.Property(p => p.City).HasColumnName("ship_to_city").HasMaxLength(100);
            a.Property(p => p.Country).HasColumnName("ship_to_country").HasMaxLength(100);
            a.Property(p => p.ContactName).HasColumnName("ship_to_contact_name").HasMaxLength(120);
            a.Property(p => p.Phone).HasColumnName("ship_to_phone").HasMaxLength(40);
        });
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        b.HasIndex(x => x.BackorderOfId);
        b.HasIndex(x => x.AssignedToId);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerAddress>().WithMany().HasForeignKey(x => x.DeliveryAddressId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Dispatch>().WithMany().HasForeignKey(x => x.BackorderOfId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasXminVersion();
    }
}

internal sealed class DispatchLineConfiguration : IEntityTypeConfiguration<DispatchLine>
{
    public void Configure(EntityTypeBuilder<DispatchLine> b)
    {
        b.ToTable("dispatch_lines", t => t.HasCheckConstraint("ck_dispatch_lines_quantities",
            "quantity > 0 AND quantity_reserved >= 0 AND quantity_picked >= 0 AND quantity_picked <= quantity_reserved " +
            "AND quantity_shipped >= 0 AND quantity_shipped <= quantity_picked AND quantity_reserved <= quantity"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EnteredQuantity).Quantity();
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.QuantityReserved).Quantity();
        b.Property(x => x.QuantityPicked).Quantity();
        b.Property(x => x.QuantityShipped).Quantity();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Ignore(x => x.Outstanding);
        b.HasIndex(x => new { x.DispatchId, x.LineNo }).IsUnique();
        b.HasIndex(x => new { x.DispatchId, x.ProductId }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DispatchStatusHistoryConfiguration : IEntityTypeConfiguration<DispatchStatusHistory>
{
    public void Configure(EntityTypeBuilder<DispatchStatusHistory> b)
    {
        b.ToTable("dispatch_status_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.DispatchId, x.At });
    }
}

internal sealed class PickConfirmationConfiguration : IEntityTypeConfiguration<PickConfirmation>
{
    public void Configure(EntityTypeBuilder<PickConfirmation> b)
    {
        b.ToTable("pick_confirmations", t => t.HasCheckConstraint("ck_pick_confirmations_quantity", "quantity <> 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ScanId).HasMaxLength(PickConfirmation.ScanIdMaxLength).IsRequired();
        // A scan id is accepted once per dispatch: a retried scan is recognised instead of counted again.
        b.HasIndex(x => new { x.DispatchId, x.ScanId }).IsUnique();
        b.HasIndex(x => x.ReservationId);
        b.HasIndex(x => x.UndoOfId).IsUnique().HasFilter("undo_of_id IS NOT NULL");
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.Barcode).HasMaxLength(64);
        b.HasOne<Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DispatchLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockReservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DispatchDiscrepancyConfiguration : IEntityTypeConfiguration<DispatchDiscrepancy>
{
    public void Configure(EntityTypeBuilder<DispatchDiscrepancy> b)
    {
        b.ToTable("dispatch_discrepancies");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Quantity).Quantity();
        b.Property(x => x.Barcode).HasMaxLength(64);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => x.DispatchId);
        b.HasOne<Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProofOfDeliveryConfiguration : IEntityTypeConfiguration<ProofOfDelivery>
{
    public void Configure(EntityTypeBuilder<ProofOfDelivery> b)
    {
        b.ToTable("proofs_of_delivery");
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiverName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.DispatchId).IsUnique();
        b.HasOne<Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.SignatureFileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> b)
    {
        b.ToTable("document_templates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Html).HasColumnType("text");
        b.Property(x => x.Comment).HasMaxLength(200);
        b.HasIndex(x => new { x.Type, x.Version }).IsUnique();
        b.HasIndex(x => x.Type).IsUnique().HasFilter("is_current");
    }
}

internal sealed class DispatchDocumentConfiguration : IEntityTypeConfiguration<DispatchDocument>
{
    public void Configure(EntityTypeBuilder<DispatchDocument> b)
    {
        b.ToTable("dispatch_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.DispatchId, x.Type });
        b.HasIndex(x => x.Status).HasFilter("status = 'Pending'");
        b.HasOne<Dispatch>().WithMany().HasForeignKey(x => x.DispatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DocumentTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        b.HasXminVersion();
    }
}

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> b)
    {
        b.ToTable("company_profile");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.AddressLine1).HasMaxLength(200);
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Website).HasMaxLength(200);
        b.Property(x => x.RegistrationNumber).HasMaxLength(60);
        b.Property(x => x.VatNumber).HasMaxLength(60);
        b.Property(x => x.FooterText).HasMaxLength(1000);
        b.Property(x => x.TimeZone).HasMaxLength(64);
        b.Property(x => x.DeliveryEmailSubject).HasMaxLength(200);
        b.Property(x => x.DeliveryEmailBody).HasMaxLength(5000);
        b.Property(x => x.DeliveryEmailBcc).HasMaxLength(500);
        b.Property(x => x.LowStockAlertEmails).HasMaxLength(500);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.LogoFileId).OnDelete(DeleteBehavior.SetNull);
        b.HasXminVersion();
    }
}

internal sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> b)
    {
        b.ToTable("email_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.To).HasMaxLength(500).IsRequired();
        b.Property(x => x.Bcc).HasMaxLength(500);
        b.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        b.Property(x => x.Body).HasColumnType("text");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.RelatedType).HasMaxLength(32);
        b.Property<List<Guid>>("_attachmentFileIds").HasColumnName("attachment_file_ids").HasColumnType("uuid[]");
        b.Ignore(x => x.AttachmentFileIds);
        b.HasIndex(x => x.NextAttemptAt).HasFilter("status = 'Pending'");
        b.HasIndex(x => new { x.RelatedType, x.RelatedId });
        b.HasXminVersion();
    }
}
