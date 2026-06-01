using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configuration;

public class RolePermissionConfig
    : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("CTbl_RolePermission");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd().IsRequired();
        builder.Property(x => x.RoleId).IsRequired();
        builder.Property(x => x.PermissionId).IsRequired();
        builder.Property(x => x.CreatedDate).IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(100);

        builder.HasOne(x => x.Roles)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Permissions)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

