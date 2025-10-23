using Dapper;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Helpers
{
    public static class SqlServerHelpers<T>
    {

        #region CacheMetada


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

        #endregion


        #region Helpers

        public static (string sql, DynamicParameters dp) BuildWhere(object? filters)
        {

            var dp = new DynamicParameters();

            if (filters == null) return ("", dp);

            var props = filters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var conds = new List<string>();

            foreach(var p in props)
            {
                var val = p.GetValue(filters);
                if (val == null) continue;

                var col = TableProps.FirstOrDefault(c =>
                   string.Equals(c.Prop.Name, p.Name, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(c.Col, p.Name, StringComparison.OrdinalIgnoreCase));

                var param = "@f_" + p.Name;
                conds.Add($"{(string.IsNullOrEmpty(col.Col) ? p.Name.ToUpperInvariant() : col.Col)} = {param}");
                dp.Add(param, val);
            }

            if (conds.Count == 0) return ("", dp);

            return ("where " + string.Join(" and ", conds),dp);
        }

        #endregion
    }
}
