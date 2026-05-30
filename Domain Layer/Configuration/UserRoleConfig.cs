using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data;

public class UserRoleConfig : IEntityTypeConfiguration<UserRole> 
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd().IsRequired();
        builder.Property(x => x.UserAccountId).IsRequired();
        builder.Property(x => x.RoleId).IsRequired();

        builder.HasOne(x => x.Roles).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Users).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}