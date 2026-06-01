using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data;

public class RoleConfig:IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(x => x.RoleId);
        builder.Property(x => x.RoleId).ValueGeneratedOnAdd().IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ModifiedBy).HasMaxLength(128);
        builder.Property(x => x.Status).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedOn).IsRequired();
        
        builder.HasIndex(x => x.RoleName).IsUnique();
    }
}