using System.Reflection;
using Core_Layer.HelperMethod;
using Domain_Layer.Database;
using Domain_Layer.DbModels;

namespace Business_Layer.Middleware;

public class PermissionGenerators
{
         public static void GetPermission(AnalysisDbContext context)
        {
            try
            {
                List<Permission> permissions = new List<Permission>();
                Assembly asm = Assembly.GetEntryAssembly();

                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in loadedAssemblies)
                {
                    var typePages = assembly.GetTypes().Where(x => x.FullName.Contains("Controllers"));

                    foreach (Type type in typePages)
                    {
                        var methods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public).ToList();
                        foreach (var method in methods)
                        {
                            var actionMethod = method.CustomAttributes.Where(x => x.AttributeType.Name == nameof(AuthorizePermission)).ToList();
                            foreach (var action in actionMethod)
                            {
                                if (action.ConstructorArguments[0].Value == null)
                                {
                                    continue;
                                }

                                string slug = action.ConstructorArguments[0].Value.ToString();

                                var slugItem = slug;
                                string parentId = "";

                                string actionName = method.Name;
                                string controllerName = method.ReflectedType.Name.Replace("Controller", "");
                                string createdBy = "System";

                                //foreach (var item in slugs)
                                //{
                                if (!context.Permissions.Any(x => x.Slug == slugItem))
                                {
                                    context.Permissions.Add(new Permission()
                                {
                                    Name = actionName + "" + controllerName,
                                    Slug = slugItem,
                                    ActionName = actionName,
                                    Controller = controllerName,
                                    MenuName = slug,
                                    CreatedBy = createdBy,
                                    CreatedDate = DateTime.Now,
                                });
                                context.SaveChanges();
                            }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }
    }

