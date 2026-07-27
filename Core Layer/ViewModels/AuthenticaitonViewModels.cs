using Domain_Layer.DbModels.Enum;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Core_Layer.ViewModels
{
    public class LoginDto
    {
        public string Email { get; set; } = "";
[NotMapped]
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }


    public class RegisterDto
    {
        [Required]
        [StringLength(120)]
        public string FullName { get; set; }



        [Required]
        [EmailAddress]
        public string Email { get; set; }
        public string? UserName { get; set; } 
        [Required]
        [MinLength(8)]
        public string Password { get; set; }

        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; }

        [Required, Phone, StringLength(20)]
        public string? Contact { get; set; }

        [StringLength(80)]
        public string? Department { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [Required, StringLength(255)]
        public string? Address { get; set; }

        [Required, StringLength(100)]
        public string? City { get; set; }

        [Required, StringLength(100)]
        public string? State { get; set; }

        [Required, StringLength(100)]
        public string? Country { get; set; }

        [Required, StringLength(20)]
        public string? PostalCode { get; set; }

        [Required]
        public int? Gender { get; set; }

        public string? ImageUrl { get; set; }
        public UserType UserType { get; set; }

        [Required(ErrorMessage = "A recent profile photo is required.")]
        public IFormFile? ProfilePhoto { get; set; }

        [Required(ErrorMessage = "A government-issued photo ID is required.")]
        public IFormFile? IdentityDocument { get; set; }
    }

    public class RegistrationEvidenceDto
    {
        public string ProfilePhotoPath { get; set; } = string.Empty;
        public string IdentityDocumentPath { get; set; } = string.Empty;
        public string IdentityDocumentOriginalName { get; set; } = string.Empty;
        public string IdentityDocumentContentType { get; set; } = string.Empty;
    }

    public class RegistrationApprovalListItemDto
    {
        public int UserAccountId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public AccountApprovalStatus ApprovalStatus { get; set; }
        public DateTime RequestedAtUtc { get; set; }
    }

    public class RegistrationApprovalDetailsDto
    {
        public int UserAccountId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string? Department { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public int? Gender { get; set; }
        public AccountApprovalStatus ApprovalStatus { get; set; }
        public DateTime RequestedAtUtc { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public string? ReviewNotes { get; set; }
        public bool HasProfilePhoto { get; set; }
        public bool HasIdentityDocument { get; set; }
        public string IdentityDocumentName { get; set; } = string.Empty;
    }
    public class LoginResponseDto
    {
        public long UserId { get; set; }
        public string UserName { get; set; }
        public string EmailAddress { get; set; }
        public List<string> Roles { get; set; }
        public string UserType { get; set; }
        public string UserImage { get; set; }
    }

    public class UserProfileDto
    {
        public int UserAccountId { get; set; }

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(120, ErrorMessage = "Full name cannot exceed 120 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [StringLength(50)]
        public string UserName { get; set; } = string.Empty;

        [StringLength(80, ErrorMessage = "Department cannot exceed 80 characters.")]
        public string? Department { get; set; }

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

        public int? Gender { get; set; }
        public string? ImageUrl { get; set; }
        public string UserType { get; set; } = string.Empty;
        public bool EmailConfirmedStatus { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
    public class UserTokens
    {
        public long UserId { get; set; }
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public string UserName { get; set; }
        public string DeviceType { get; set; }
        public bool UserStatus { get; set; }
        public string UserType { get; set; }
        public string ExpiryTimeUtc { get; set; }
    }

}
