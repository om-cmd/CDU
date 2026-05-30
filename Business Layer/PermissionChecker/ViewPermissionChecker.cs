using Core_Layer.HelperMethod;
using Microsoft.Data.SqlClient;

namespace Business_Layer;

public static class ViewPermissionChecker
{
    public static bool HasPermission(long userId, string permissionValue)
    {
        try
        {
            using (SqlConnection con = new SqlConnection(DefaultConfiguration.ConnectionString))
            {
                using (SqlCommand cmd = con.CreateCommand())
                {
                    cmd.Connection = con;
                    cmd.CommandText = $"select Case when ISNULL(p.Slug, '') = '' then 0 Else 1 End as HasPermission from CTbl_Permissions p join CTbl_RolePermission rP on p.Id = rP.PermissionId join CTbl_UserRoles uR " +
                                      $"on rP.RoleId = uR.RoleId join CTbl_Users u on uR.UserId = u.UserId where u.UserId = {userId} and p.Slug = '{permissionValue}'";

                    cmd.CommandType = System.Data.CommandType.Text;
                    cmd.Parameters.AddWithValue("userId", userId);
                    cmd.Parameters.AddWithValue("permission", permissionValue);
                    
                    con.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                bool hasPermission = Convert.ToBoolean(reader["HasPermission"]);
                                return hasPermission;
                            }
                        }
                    }
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            return false;
        }
    }
}