using System.Security.Claims;
using Core_Layer.HelperMethod;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore.Infrastructure;
using IAuthenticationService = Analysis_Web.Services.IAuthenticationService;

namespace Business_Layer.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUnitOfWork _unitOfWork;
    public AuthenticationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<(string Message, bool Success, string Status)> RegisterUserAsync(RegisterDto model)
    {
        try
        {
            var validate = await ValidateUser(model);
            if (!validate.Success)
            {
                return (validate.Message, validate.Success, validate.Status);
            }

            if (model.UserType == 0)
            {
                model.UserType = UserType.User;
            }
            model.Password = StaticMethods.HashPassword(model.Password).Item2;
            if (model.UserType == UserType.User)
            {
                var transaction = _unitOfWork._db.Database.BeginTransaction();
                try
                {
 var users = new ApplicationUser
                {
                    FullName = model.FullName,
                    UserName = model.Email,
                    Email = model.Email,
                    Password = model.Password,
                    Contact = model.Contact,
                    Department = model.Department,
                    DateOfBirth = model.DateOfBirth,
                    Address = model.Address,
                    City = model.City,
                    State = model.State,
                    Country = model.Country,
                    PostalCode = model.PostalCode,
                    Gender = model.Gender,
                    ImageUrl = model.ImageUrl,
                    UserType = model.UserType,
                    IsActive = true,
                    IsValidated = false,
                    PhoneValidated = false,
                    EmailConfirmedStatus = false,
                    CreatedAt = DateTime.UtcNow
                };
                _unitOfWork.Users.Add(users);
                await _unitOfWork.SaveChangesAsync();

                if (_unitOfWork._db.Roles.Any(x => x.RoleName.ToLower() == "user"))
                {
                    var role = _unitOfWork._db.Roles.FirstOrDefault(x => x.RoleName.ToLower() == "user");
                    _unitOfWork._db.UserRoles.Add(new UserRole()
                    {
                        CreatedBy =  "System",
                        RoleId =  role.RoleId,
                        UserAccountId = users.UserAccountId,
                        CreatedDate =  DateTime.UtcNow
                    });
                    await _unitOfWork.SaveChangesAsync();
                }
                else
                {
                    var role = new Role
                    {
                        RoleName = "User",
                        RoleDescription = "For Normal User!!!",
                        CreatedBy = "System",
                        CreatedOn = DateTime.UtcNow,
                        ModifiedBy = "System",
                        ModifiedOn = DateTime.UtcNow,
                        Status = "Active"
                    };
                    _unitOfWork._db.Roles.Add(role);
                    await _unitOfWork.SaveChangesAsync();
                    _unitOfWork._db.UserRoles.Add(new UserRole()
                    {
                        CreatedBy =  "System",
                        RoleId =  role.RoleId,
                        UserAccountId = users.UserAccountId,
                        CreatedDate =  DateTime.UtcNow
                    });
                    await _unitOfWork.SaveChangesAsync();
                }
                transaction.Commit();
                return ("User Resgitered Successfully!!",true, "00");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return ("Unable to add user!!",false, "1");

                }
               
            }
            else
            {
                var users = new ApplicationUser
                {
                    FullName = model.FullName,
                    UserName = model.Email,
                    Email = model.Email,
                    Password = model.Password,
                    Contact = model.Contact,
                    Department = model.Department,
                    DateOfBirth = model.DateOfBirth,
                    Address = model.Address,
                    City = model.City,
                    State = model.State,
                    Country = model.Country,
                    PostalCode = model.PostalCode,
                    Gender = model.Gender,
                    ImageUrl = model.ImageUrl,
                    UserType = model.UserType,
                    IsActive = true,
                    IsValidated = false,
                    PhoneValidated = false,
                    EmailConfirmedStatus = false,
                    CreatedAt = DateTime.UtcNow
                };
                _unitOfWork.Users.Add(users);
                await _unitOfWork.SaveChangesAsync();
            
                return ("User Resgitered Successfully!!",true, "00");
            }
          
        }
        catch (Exception ex)
        {

            return (ex.Message.ToString(),false, "1");
        }
        
    }

    public async Task<(bool Success, string Message, string Status)> ValidateUser(RegisterDto user)
    {
        try
        {
            var users = _unitOfWork.Users.ToList();
            
            if (!users.Any())
            {
                if (!users.Any(x => x.Email == user.Email))
                {
                    return (false, "Email Already Exists!!!", "1");
                }                
                if (!users.Any(x => x.Contact == user.Contact))
                {
                    return (false, "Contact Already Exists!!!", "1");
                }
                if (!users.Any(x => x.UserName == user.UserName))
                {
                    return (false, "UserName is Already Taken!!!", "1");
                }
                return (true, "ValidateSucessFully", "00");
            }
            return (true, "ValidateSucessFully", "00");

        }
        catch (Exception ex)
        {
            
            return (false, ex.Message, "1");
            
        }

    }

    public async Task<(string Message, bool Success, string Status, LoginResponseDto)> Login(LoginDto model)
    {
        try
        {
            if (string.IsNullOrEmpty(model.Password))
                return ("PASSWORD IS REQUIRED!!", false, "1",null);
            if (string.IsNullOrEmpty(model.Email))
                return ("EMAIL IS REQUIRED!!", false, "1",null);
            if(!_unitOfWork.Users.Any(x => x.Email == model.Email)||_unitOfWork.Users.Any(x => x.UserName == model.UserName))
                return ("NO USER FOUND PLESE REGISTER!!!",false,"404",null);
            var user = _unitOfWork.Users.FirstOrDefault(x => x.Email == model.Email || x.UserName == model.UserName);
            if (!StaticMethods.VerifyPassword(model.Password, user.Password))
            {
                return ("USERNAME OR PASSWORD MISMATCHED!!!",false, "1",null);                
            }
             var tokens = new LoginResponseDto()
             {
                 EmailAddress = user.Email,
                 UserName = user.UserName,
                 Roles = null,
                 UserId = user.UserAccountId,
                 UserType = user.UserType.ToString(),

             };
             var token = StaticMethods.GenTokenkey(tokens);
             var auth = new AuthenticationProperties
             {
                 AllowRefresh = true,
                 ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                 IssuedUtc = DateTimeOffset.UtcNow,
                 IsPersistent = true
             };

             var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

             identity.AddClaim(new Claim(ClaimTypes.Name, user.FullName ?? ""));
             identity.AddClaim(new Claim(ClaimTypes.Actor, user.Email ?? ""));
             identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserAccountId.ToString()));
             identity.AddClaim(new Claim("Token", token.data.Token));
             identity.AddClaim(new Claim("RToken", token.data.RefreshToken));
             identity.AddClaim(new Claim(ClaimTypes.Role, user.UserType.ToString()));

             await _unitOfWork.HttpContextAccessor.HttpContext!.SignInAsync(
                 CookieAuthenticationDefaults.AuthenticationScheme,
                 new ClaimsPrincipal(identity),
                 auth);
             LoginResponseDto dto = new LoginResponseDto()
             {
                 EmailAddress = user.Email,
                 UserName = user.UserName,
                 Roles = null,
                 UserId = user.UserAccountId,
                 UserType = user.UserType.ToString(),
             };
             return ("Login Success", true, "00", dto);
        }
        catch (Exception ex)
        {
            return (ex.Message.ToString(),false, "1",null);   
        }
    }

    public async void Logout()
    {
        await _unitOfWork.HttpContextAccessor.HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        _unitOfWork.HttpContextAccessor.HttpContext.Session.Clear();
    }
}