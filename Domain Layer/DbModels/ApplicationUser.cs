using Domain_Layer.DbModels.Enum;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain_Layer.DbModels
{
    public class ApplicationUser 
    {
        [Key]
        public int UserAccountId { get; set; }


        [Required]
        [StringLength(120, ErrorMessage = "Full name cannot exceed 120 characters.")]
        public string FullName { get; set; } = string.Empty;

        [StringLength(80, ErrorMessage = "Department cannot exceed 80 characters.")]
        public string? Department { get; set; }

        [Required]
        [StringLength(50, ErrorMessage = "Username cannot exceed 50 characters.")]
        public  string UserName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters.")]
        public  string Email { get; set; } = string.Empty;

        [Phone]
        [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
        public string? Contact { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [StringLength(255, ErrorMessage = "Address cannot exceed 255 characters.")]
        public string? Address { get; set; }

        [StringLength(100, ErrorMessage = "City cannot exceed 100 characters.")]
        public string? City { get; set; }

        [StringLength(100, ErrorMessage = "State cannot exceed 100 characters.")]
        public string? State { get; set; }

        [StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
        public string? Country { get; set; }

        [StringLength(20, ErrorMessage = "Postal code cannot exceed 20 characters.")]
        public string? PostalCode { get; set; }


        [Required]
        [PasswordPropertyText(true)]
        [StringLength(255, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        [NotMapped]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsValidated { get; set; } = false;

        public bool PhoneValidated { get; set; } = false;

        public bool EmailConfirmedStatus { get; set; } = false;

        public UserType UserType { get; set; }

        public int? Gender { get; set; }


        [StringLength(255, ErrorMessage = "Image URL cannot exceed 255 characters.")]
        public string? ImageUrl { get; set; }


        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public DateTime? DateModified { get; set; }


        [StringLength(255)]
        public string? DeviceInfo { get; set; }

        public bool Deleted { get; set; } = false;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid RowStamp { get; set; } = Guid.NewGuid();
    }
}