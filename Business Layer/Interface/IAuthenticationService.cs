using Core_Layer.ViewModels;
using Domain_Layer.DbModels;

namespace Analysis_Web.Services;

public interface IAuthenticationService
{
    Task<(string Message, bool Success, string Status)> RegisterUserAsync(RegisterDto model);
    Task<(bool Success, string Message, string Status)> ValidateUser(RegisterDto user);
    Task<(string Message, bool Success, string Status,LoginResponseDto)> Login(LoginDto model);
    void Logout();
}
