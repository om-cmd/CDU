using Domain_Layer.DbModels.Enum;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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
        public string FullName { get; set; }



        [Required]
        [EmailAddress]
        public string Email { get; set; }
        public string? UserName { get; set; } 
        [Required]
        [MinLength(6)]
        public string Password { get; set; }

        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; }

        public string? Contact { get; set; }

        public string? Department { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        public string? Country { get; set; }

        public string? PostalCode { get; set; }

        public int? Gender { get; set; }

        public string? ImageUrl { get; set; }
        public UserType UserType { get; set; }
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
