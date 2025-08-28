using Dapper;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Oracle.Infractructure.Helpers
{
    public static class OracleHelpers
    {
        
        public static void RegisterFromAssemblies(params Assembly[] assemblies)
        {
            foreach (var asm in assemblies)
            {
                foreach (var type in asm.GetTypes())
                {
                    // Preskoči tipove koji nisu class ili su generički/open
                    if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
                        continue;

                    // Da li tip uopšte koristi [Column] na nekom svojstvu?
                    var propsWithColumn = type.GetProperties()
                        .Where(p => p.GetCustomAttributes<ColumnAttribute>(inherit: true).Any())
                        .ToArray();

                    if (propsWithColumn.Length == 0)
                        continue;

                    // Registruj CustomPropertyTypeMap zasnovan na [Column] atributima
                    SqlMapper.SetTypeMap(type,
                        new CustomPropertyTypeMap(
                            type,
                            (mappedType, columnName) =>
                                mappedType .GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(p =>p.GetCustomAttributes<ColumnAttribute>(true)
                                         .Any(a => string.Equals(a.Name, columnName,
                                                 StringComparison.OrdinalIgnoreCase)))
                        )
                    );
                }
            }
        }

        
        public static void RegisterFrom<TMarker>()
            => RegisterFromAssemblies(typeof(TMarker).Assembly);
    }


    public static class OracleHelpers<T>
    {
        //private static readonly Lazy<IReadOnlyList<(PropertyInfo Prop, string Col, bool IsKey, bool IsIdentity)>> _tableProps =
        //    new(() =>
        //    {
        //        var toRet = new List<(PropertyInfo p, string c, bool k, bool i)>();
        //        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        //        {
        //            var col = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
        //            var key = p.GetCustomAttribute<KeyAttribute>() is not null || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase);
        //            var identity = p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
        //            toRet.Add((p, col, key, identity));
        //        }
        //        return toRet;
        //    });

        private static readonly Lazy<(PropertyInfo Prop, string Col, bool IsKey, bool IsIdentity)[]> _tableProps =
                new(() =>
                {
                    return typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Select(p =>
                        {
                            var col = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
                            var key = p.GetCustomAttribute<KeyAttribute>() is not null || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase);
                            var identity = p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
                            return (p, col, key, identity);
                        })
                        .ToArray();
                }, LazyThreadSafetyMode.ExecutionAndPublication);

        public static IReadOnlyList<(PropertyInfo Prop, string Col, bool IsKey, bool IsIdentity)> TableProps => _tableProps.Value;
    }
}
