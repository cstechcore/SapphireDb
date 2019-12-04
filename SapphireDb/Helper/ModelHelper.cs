﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Metadata;
using Newtonsoft.Json.Linq;
using SapphireDb.Attributes;
using SapphireDb.Internal;
using SapphireDb.Models;
using SapphireDb.Models.Auth;

namespace SapphireDb.Helper
{
    static class ModelHelper
    {
        public static object[] GetPrimaryKeyValues(this Type type, SapphireDbContext db, Dictionary<string, JValue> primaryKeys)
        {
            return type.GetPrimaryKeys(db)
                .Select(p => primaryKeys[p.Name.ToCamelCase()].ToObject(p.ClrType)).ToArray();
        }

        public static object[] GetPrimaryKeyValues(this Type type, SapphireDbContext db, object entityObject)
        {
            return type.GetPrimaryKeys(db)
                .Select(p => p.PropertyInfo.GetValue(entityObject)).ToArray();
        }

        public static string[] GetPrimaryKeyNames(this Type type, SapphireDbContext db)
        {
            return type.GetPrimaryKeys(db).Select(p => p.Name.ToCamelCase()).ToArray();
        }

        private static readonly object PrimaryKeyLock = new object();
        private static readonly Dictionary<Type, IProperty[]> PrimaryKeyDictionary = new Dictionary<Type, IProperty[]>();

        public static IProperty[] GetPrimaryKeys(this Type type, SapphireDbContext db)
        {
            lock (PrimaryKeyLock)
            {
                if (PrimaryKeyDictionary.TryGetValue(type, out IProperty[] primaryKeys))
                {
                    return primaryKeys;
                }

                primaryKeys = db.Model.FindEntityType(type.FullName).FindPrimaryKey().Properties.ToArray();
                PrimaryKeyDictionary.Add(type, primaryKeys);

                return primaryKeys;
            }
        }

        private static readonly object PropertyInfosLock = new object();
        private static readonly Dictionary<Type, AuthPropertyInfo[]> PropertyInfosDictionary = new Dictionary<Type, AuthPropertyInfo[]>();

        public static AuthPropertyInfo[] GetAuthPropertyInfos(this Type entityType)
        {
            lock (PropertyInfosLock)
            {
                if (PropertyInfosDictionary.TryGetValue(entityType, out AuthPropertyInfo[] propertyInfos))
                {
                    return propertyInfos;
                }

                propertyInfos = entityType.GetProperties().Select(p => new AuthPropertyInfo(p)).ToArray();
                PropertyInfosDictionary.Add(entityType, propertyInfos);

                return propertyInfos;
            }
        }

        public static void UpdateFields(this Type entityType, object entityObject, object newValues,
            SapphireDbContext db, HttpInformation information, IServiceProvider serviceProvider)
        {
            string[] primaryKeys = entityType.GetPrimaryKeyNames(db);
            bool isClassUpdateable = entityType.GetCustomAttribute<UpdatableAttribute>() != null;
            
            foreach (AuthPropertyInfo pi in entityType.GetAuthPropertyInfos())
            {
                if ((isClassUpdateable || pi.UpdatableAttribute != null) && !primaryKeys.Contains(pi.PropertyInfo.Name.ToCamelCase()))
                {
                    if (pi.CanUpdate(information, entityObject, serviceProvider))
                    {
                        pi.PropertyInfo.SetValue(entityObject, pi.PropertyInfo.GetValue(newValues));
                    }
                }
            }
        }

        public static async Task<Dictionary<string, object>> GenerateUserData(IdentityUser identityUser, AuthDbContextTypeContainer typeContainer, object usermanager)
        {
            Dictionary<string, object> userData = new Dictionary<string, object>();
            Type t = identityUser.GetType();

            IEnumerable<PropertyInfo> properties =
                t.GetProperties().Where(p => p.GetCustomAttribute<AuthUserInformationAttribute>() != null
                || p.Name == "Id" || p.Name == "UserName" || p.Name == "Email");

            foreach (PropertyInfo property in properties)
            {
                if (property.Name == "Roles")
                {
                    userData["_Roles"] = property.GetValue(identityUser);
                }
                else
                {
                    userData[property.Name] = property.GetValue(identityUser);
                    
                }
            }

            userData["Roles"] =
                await(dynamic)typeContainer.UserManagerType.GetMethod("GetRolesAsync").Invoke(usermanager, new object[] { identityUser });

            return userData;
        }

        public static IEnumerable<Dictionary<string, object>> GetUsers(ISapphireAuthContext db,
            AuthDbContextTypeContainer typeContainer, object usermanager)
        {
            IEnumerable<IdentityUser> users = (IQueryable<IdentityUser>)typeContainer
                .UserManagerType.GetProperty("Users").GetValue(usermanager);

            
            IEnumerable<Dictionary<string, object>> usersConverted = users
                .Select(u => GenerateUserData(u, typeContainer, usermanager).Result);

            return usersConverted;
        }

        public static Dictionary<string, object> GenerateRoleData(IdentityRole identityRole, 
            IEnumerable<IdentityUserRole<string>> userRoles = null)
        {
            return new Dictionary<string, object>
            {
                ["Id"] = identityRole.Id,
                ["Name"] = identityRole.Name,
                ["NormalizedName"] = identityRole.NormalizedName,
                ["UserIds"] = userRoles?.Where(ur => ur.RoleId == identityRole.Id).Select(ur => ur.UserId)
            };
        }

        public static IEnumerable<Dictionary<string, object>> GetRoles(ISapphireAuthContext db)
        {
            IEnumerable<IdentityUserRole<string>> userRoles = db.UserRoles;

            return db.Roles.ToList().Select(r => GenerateRoleData(r, userRoles));
        }

        public static IEnumerable<object> GetValues(this SapphireDbContext db, KeyValuePair<Type, string> property, IServiceProvider serviceProvider, HttpInformation httpInformation)
        {
            IEnumerable<object> values = (IEnumerable<object>)db.GetType().GetProperty(property.Value)?.GetValue(db);

            QueryFunctionAttribute queryFunctionAttribute = property.Key.GetCustomAttribute<QueryFunctionAttribute>();
            if (queryFunctionAttribute != null)
            {
                var queryFunctionInfo = property.Key.GetMethod(queryFunctionAttribute.Function);

                if (queryFunctionInfo != null)
                {
                    object[] methodParameters = queryFunctionInfo.CreateParameters(httpInformation, serviceProvider);
                    object queryFunctionRaw = queryFunctionInfo.Invoke(null, methodParameters);
                    MethodInfo invokeMethod = queryFunctionRaw.GetType().GetMethod("Invoke");

                    if (invokeMethod != null)
                    {
                        ParameterExpression valueParameter = Expression.Parameter(typeof(object));
                        Func<object, bool> queryFunction = Expression.Lambda<Func<object, bool>>(
                            Expression.Call(
                                Expression.Constant(queryFunctionRaw),
                                invokeMethod,
                                Expression.Convert(valueParameter, property.Key)
                            ),
                            valueParameter).Compile();

                        values = values?.Where(queryFunction);
                    }
                }
            }

            IProperty[] primaryProperties = property.Key.GetPrimaryKeys(db);

            for (int i = 0; i < primaryProperties.Length; i++)
            {
                PropertyInfo pi = primaryProperties[i].PropertyInfo;

                values = i == 0 ? values?.OrderBy(o => pi.GetValue(o))
                    : ((IOrderedEnumerable<object>)values)?.ThenBy(o => pi.GetValue(o));
            }

            return values;
        }

        public static void ExecuteHookMethod<T>(this Type modelType, Func<ModelStoreEventAttributeBase, string> methodSelector,
            object newValue, HttpInformation httpInformation, IServiceProvider serviceProvider) where T : ModelStoreEventAttributeBase
        {
            T attribute = modelType.GetCustomAttribute<T>();

            if (attribute != null)
            {
                string methodName = methodSelector(attribute);

                if (!string.IsNullOrEmpty(methodName))
                {
                    MethodInfo methodInfo = modelType.GetMethod(methodName,
                        BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (methodInfo != null && methodInfo.ReturnType == typeof(void))
                    {
                        methodInfo.Invoke(newValue, methodInfo.CreateParameters(httpInformation, serviceProvider));
                    }
                }
            }
        }
    }
}