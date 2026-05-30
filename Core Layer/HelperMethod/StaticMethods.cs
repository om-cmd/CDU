using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Core_Layer.ViewModels;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace Core_Layer.HelperMethod;

public static class StaticMethods
{
    private static string ciphers = "!@Parlad@#1";

    public static Tuple<string, string> HashPassword(string password)
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        string hashPassword = BCrypt.Net.BCrypt.HashPassword(password, salt);
        return new Tuple<string, string>(hashPassword, salt);
    }

    public static bool VerifyPassword(string password, string hashedPassword)
    {
        return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }

    #region JWT Related

    public static string EncryptUserData(LoginResponseDto user, string issueDate)
    {
        DateTime issuedDateS = DateTime.Parse(issueDate);

        string jsonData = JsonConvert.SerializeObject(user);
        byte[] inputArray = Encoding.UTF8.GetBytes(jsonData);

        using TripleDES tripleDes = TripleDES.Create();

        tripleDes.Key = Encoding.UTF8.GetBytes(ciphers + issuedDateS.ToString("ssmmhhddMMyy"));
        tripleDes.Mode = CipherMode.ECB;
        tripleDes.Padding = PaddingMode.PKCS7;

        ICryptoTransform cryptoTransform = tripleDes.CreateEncryptor();
        byte[] resultArray = cryptoTransform.TransformFinalBlock(inputArray, 0, inputArray.Length);

        return Convert.ToBase64String(resultArray);
    }
    public static List<Claim> GetClaims(LoginResponseDto model)
    {
        string issuedDate = DateTime.Now.ToString("G");
        model.UserImage = string.IsNullOrEmpty(model.UserImage) ? "user.png" : model.UserImage;
        var userData = StaticMethods.EncryptUserData(model, issuedDate);
        var claims = new List<Claim>
        {
            new Claim("UserToken", userData),
            new Claim("IssuedDate", issuedDate),
            new Claim(ClaimTypes.Name, model.EmailAddress??model.EmailAddress),
        };
        return claims;
    }

    public static string GetRToken()
    {
        return Guid.NewGuid().ToString("N");
    }
public static (UserTokens data,string status,bool sucess) GenTokenkey(LoginResponseDto model)
        {
            try
            {
                UserTokens response = new UserTokens();
                response = new UserTokens();
                if (model == null) throw new ArgumentException(nameof(model));
                // Get secret key
                var key = Encoding.ASCII.GetBytes(DefaultConfiguration.staticConfiguration.GetSection("JsonWebTokenKeys:IssuerSigningKey").Value);
                DateTime notBefore = new DateTimeOffset(DateTime.Now).DateTime;
                DateTime expires = new DateTimeOffset(DateTime.Now.AddMinutes(Convert.ToInt32(DefaultConfiguration.staticConfiguration.GetSection("JsonWebTokenKeys:ValidationLifeTimeInMin").Value ?? "5"))).DateTime;

                response.ExpiryTimeUtc = expires.ToUniversalTime().ToString("s");
                var JWToken = new JwtSecurityToken(
                    issuer: DefaultConfiguration.staticConfiguration.GetSection("JsonWebTokenKeys:ValidIssuer").Value,
                    audience: DefaultConfiguration.staticConfiguration.GetSection("JsonWebTokenKeys:ValidAudience").Value,
                    claims: GetClaims(model),
                    notBefore: notBefore,
                    expires: expires,
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
                response.Token = new JwtSecurityTokenHandler().WriteToken(JWToken);
                response.RefreshToken = GetRToken();
                response.UserName = model.EmailAddress;
                response.UserId = model.UserId;
                response.UserType = model.UserType;
                response.UserStatus = true;
                return (response, "00", true);

            }
            catch (Exception ex)
            {
                return (null, ex.Message, false);
            }
        }
    public static LoginResponseDto? DecryptUserData(string encryptedData, string issueDate)
    {
        DateTime issuedDateS = DateTime.Parse(issueDate);

        byte[] inputArray = Convert.FromBase64String(encryptedData);

        using TripleDES tripleDes = TripleDES.Create();

        tripleDes.Key = Encoding.UTF8.GetBytes(ciphers + issuedDateS.ToString("ssmmhhddMMyy"));
        tripleDes.Mode = CipherMode.ECB;
        tripleDes.Padding = PaddingMode.PKCS7;

        ICryptoTransform cryptoTransform = tripleDes.CreateDecryptor();
        byte[] resultArray = cryptoTransform.TransformFinalBlock(inputArray, 0, inputArray.Length);

        string jsonData = Encoding.UTF8.GetString(resultArray);

        return JsonConvert.DeserializeObject<LoginResponseDto>(jsonData);
    }

    public static (LoginResponseDto? Data, string Status, string Message) ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, "401", "Invalid token provided");
        }

        token = token.Replace("Bearer ", "");

        var handler = new JwtSecurityTokenHandler();

        var secretKey = DefaultConfiguration.staticConfiguration
            .GetSection("JsonWebTokenKeys:IssuerSigningKey").Value;

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return (null, "500", "JWT signing key is missing");
        }

        var key = Encoding.UTF8.GetBytes(secretKey);

        try
        {
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = DefaultConfiguration.staticConfiguration
                    .GetSection("JsonWebTokenKeys:ValidIssuer").Value,
                ValidateAudience = true,
                ValidAudience = DefaultConfiguration.staticConfiguration
                    .GetSection("JsonWebTokenKeys:ValidAudience").Value,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;

            string? userToken = jwtToken.Claims
                .FirstOrDefault(x => x.Type == "UserToken")?.Value;

            string? issuedDate = jwtToken.Claims
                .FirstOrDefault(x => x.Type == "IssuedDate")?.Value;

            if (string.IsNullOrWhiteSpace(userToken) || string.IsNullOrWhiteSpace(issuedDate))
            {
                return (null, "401", "Required token claims are missing");
            }

            var userData = DecryptUserData(userToken, issuedDate);

            return (userData, "200", "Token parsed successfully");
        }
        catch (SecurityTokenExpiredException)
        {
            return (null, "401", "Token is expired");
        }
        catch
        {
            return (null, "401", "Invalid token provided");
        }
    }

    #endregion
}