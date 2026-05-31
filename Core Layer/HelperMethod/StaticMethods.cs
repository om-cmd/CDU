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
    private static readonly string ciphers = "!@Parlad@#1MySecretKey12";

    public static Tuple<string, string> HashPassword(string plainText)
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(12);
        string hashPassword = BCrypt.Net.BCrypt.HashPassword(plainText, salt);
        return Tuple.Create(salt, hashPassword);
    }

    public static bool VerifyPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }

    private static byte[] GetTripleDesKey(string issueDate)
    {
        DateTime issuedDateS = DateTime.Parse(issueDate);

        var rawKey = ciphers + issuedDateS.ToString("ssmmhhddMMyy");

        using var sha256 = SHA256.Create();

        return sha256
            .ComputeHash(Encoding.UTF8.GetBytes(rawKey))
            .Take(24)
            .ToArray();
    }

    public static string EncryptUserData(LoginResponseDto user, string issueDate)
    {
        string jsonData = JsonConvert.SerializeObject(user);
        byte[] inputArray = Encoding.UTF8.GetBytes(jsonData);

        using TripleDES tripleDes = TripleDES.Create();

        tripleDes.Key = GetTripleDesKey(issueDate);
        tripleDes.Mode = CipherMode.ECB;
        tripleDes.Padding = PaddingMode.PKCS7;

        using ICryptoTransform cryptoTransform = tripleDes.CreateEncryptor();

        byte[] resultArray = cryptoTransform.TransformFinalBlock(
            inputArray,
            0,
            inputArray.Length
        );

        return Convert.ToBase64String(resultArray);
    }

    public static List<Claim> GetClaims(LoginResponseDto model)
    {
        string issuedDate = DateTime.Now.ToString("G");

        model.UserImage = string.IsNullOrEmpty(model.UserImage)
            ? "user.png"
            : model.UserImage;

        var userData = EncryptUserData(model, issuedDate);

        return new List<Claim>
        {
            new Claim("UserToken", userData),
            new Claim("IssuedDate", issuedDate),
            new Claim(ClaimTypes.Name, model.EmailAddress ?? string.Empty)
        };
    }

    public static string GetRToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static (UserTokens? data, string status, bool success) GenTokenkey(LoginResponseDto model)
    {
        try
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var configuration = DefaultConfiguration.StaticConfiguration
                ?? throw new InvalidOperationException("Configuration is not initialized.");

            var issuerSigningKey = configuration["JsonWebTokenKeys:IssuerSigningKey"];

            if (string.IsNullOrWhiteSpace(issuerSigningKey))
                throw new InvalidOperationException("JWT IssuerSigningKey is missing.");

            var key = Encoding.UTF8.GetBytes(issuerSigningKey);

            var validationMinutesText = configuration["JsonWebTokenKeys:ValidationLifeTimeInMin"];

            int validationMinutes = string.IsNullOrWhiteSpace(validationMinutesText)
                ? 5
                : Convert.ToInt32(validationMinutesText);

            DateTime issueDate = DateTime.Now;
            DateTime expires = issueDate.AddMinutes(validationMinutes);

            var response = new UserTokens
            {
                ExpiryTimeUtc = expires.ToUniversalTime().ToString("s"),
                RefreshToken = GetRToken(),
                UserName = model.EmailAddress,
                UserId = model.UserId,
                UserType = model.UserType,
                UserStatus = true
            };

            var jwtToken = new JwtSecurityToken(
                issuer: configuration["JsonWebTokenKeys:ValidIssuer"],
                audience: configuration["JsonWebTokenKeys:ValidAudience"],
                claims: GetClaims(model),
                notBefore: issueDate,
                expires: expires,
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256)
            );

            response.Token = new JwtSecurityTokenHandler().WriteToken(jwtToken);

            return (response, "00", true);
        }
        catch
        {
            return (null, "1", false);
        }
    }

    public static LoginResponseDto? DecryptUserData(string encryptedData, string issueDate)
    {
        byte[] inputArray = Convert.FromBase64String(encryptedData);

        using TripleDES tripleDes = TripleDES.Create();

        tripleDes.Key = GetTripleDesKey(issueDate);
        tripleDes.Mode = CipherMode.ECB;
        tripleDes.Padding = PaddingMode.PKCS7;

        using ICryptoTransform cryptoTransform = tripleDes.CreateDecryptor();

        byte[] resultArray = cryptoTransform.TransformFinalBlock(
            inputArray,
            0,
            inputArray.Length
        );

        string jsonData = Encoding.UTF8.GetString(resultArray);

        return JsonConvert.DeserializeObject<LoginResponseDto>(jsonData);
    }

    public static (LoginResponseDto? Data, string Status, string Message) ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, "401", "Invalid token provided");
        }

        token = token.Replace("Bearer ", "").Trim();

        var configuration = DefaultConfiguration.StaticConfiguration;

        if (configuration == null)
        {
            return (null, "500", "Configuration is not initialized");
        }

        var secretKey = configuration["JsonWebTokenKeys:IssuerSigningKey"];
        var validIssuer = configuration["JsonWebTokenKeys:ValidIssuer"];
        var validAudience = configuration["JsonWebTokenKeys:ValidAudience"];

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return (null, "500", "JWT signing key is missing");
        }

        var key = Encoding.UTF8.GetBytes(secretKey);

        try
        {
            var handler = new JwtSecurityTokenHandler();

            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),

                ValidateIssuer = true,
                ValidIssuer = validIssuer,

                ValidateAudience = true,
                ValidAudience = validAudience,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken)
            {
                return (null, "401", "Invalid token type");
            }

            string? userToken = jwtToken.Claims
                .FirstOrDefault(x => x.Type == "UserToken")?.Value;

            string? issuedDate = jwtToken.Claims
                .FirstOrDefault(x => x.Type == "IssuedDate")?.Value;

            if (string.IsNullOrWhiteSpace(userToken) || string.IsNullOrWhiteSpace(issuedDate))
            {
                return (null, "401", "Required token claims are missing");
            }

            var userData = DecryptUserData(userToken, issuedDate);

            if (userData == null)
            {
                return (null, "401", "Unable to decrypt user data");
            }

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
}