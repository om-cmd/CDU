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
