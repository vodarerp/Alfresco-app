using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Threading;
using SqlServer.Infrastructure.Helpers;
using System.Data;



namespace SqlServer.Infrastructure.Implementation
{
    public class SqlServerRepository<T, TKey> : IRepository<T, TKey>
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly int _commandTimeoutSeconds;
        protected IDbConnection Conn => _unitOfWork.Connection;
        protected IDbTransaction Tx => _unitOfWork.Transaction;

        public SqlServerRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions)
        {
            _unitOfWork = uow;
            _commandTimeoutSeconds = sqlServerOptions.CommandTimeoutSeconds;
        }


        public string TableName => typeof(T).GetCustomAttributes<TableAttribute>()?.FirstOrDefault()?.Name ?? typeof(T).Name;


        private IEnumerable<(PropertyInfo Prop, string Col, bool IsKey, bool IsIdentity)> GetColumns()
        {
            foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var col = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
                var key = p.GetCustomAttribute<KeyAttribute>() is not null || string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase);
                var identity = p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
                yield return (p, col, key, identity);
            }
        }

        public async Task<TKey> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            var columns = SqlServerHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));
            var paramNames = string.Join(", ", columns.Select(c => $"@{c.Col}"));
            var idCol = GetColumns().FirstOrDefault(o => o.IsKey).Col ?? "Id";

            // SQL Server uses OUTPUT instead of RETURNING
            string sql = $"INSERT INTO {TableName} ({colNames}) OUTPUT INSERTED.{idCol} VALUES ({paramNames})";
            var parameters = new DynamicParameters();
            foreach (var (Prop, Col, IsKey, IsIdentity) in columns)
            {
                var val = Prop.GetValue(entity);
                parameters.Add($"@{Col}", val);
            }

            var outVal = await Conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, parameters, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return (TKey)Convert.ChangeType(outVal, typeof(TKey));
        }

        public async Task<int> InsertManyAsync(IEnumerable<T> entities, CancellationToken ct = default)
        {
            var listEntities = entities.ToList();
            if (listEntities.Count == 0) return 0;

            var columns = SqlServerHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
            var colNames = string.Join(", ", columns.Select(c => c.Col));
            var paramNames = string.Join(", ", columns.Select(c => $"@{c.Col}"));

            string sql = $"INSERT INTO {TableName} ({colNames}) VALUES ({paramNames})";

            var batchSize = 1000;
            int totalInserted = 0;

            for (int offset = 0; offset < listEntities.Count; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = listEntities.Skip(offset).Take(batchSize).ToList();

                // Use parameterized batch insert for SQL Server
                var batchParameters = new List<DynamicParameters>();
                foreach (var entity in batch)
                {
                    var dp = new DynamicParameters();
                    foreach (var col in columns)
                    {
                        var val = col.Prop.GetValue(entity);
                        dp.Add($"@{col.Col}", val);
                    }
                    batchParameters.Add(dp);
                }

                // Execute batch
                foreach (var param in batchParameters)
                {
                    var cmd = new CommandDefinition(sql, param, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
                    totalInserted += await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
                }
            }

            return totalInserted;
        }

        public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = SqlServerHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            var sql = $"DELETE FROM {TableName} WHERE {key.Col} = @id";

            var dp = new DynamicParameters();
            dp.Add("@id", id);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            var key = SqlServerHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            string sql = $"SELECT * FROM {TableName} WHERE {key.Col} = @id";

            var dp = new DynamicParameters();
            dp.Add("@id", id);
            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken);
            return await Conn.QueryFirstOrDefaultAsync<T>(cmd).ConfigureAwait(false);
        }

        public async Task<IEnumerable<T>> GetListAsync(object? filters = null, int? skip = null, int? take = null, string[]? orderBy = null, CancellationToken ct = default)
        {
            var (whereSql, dp) = SqlServerHelpers<T>.BuildWhere(filters);

            var sql = $"SELECT * FROM {TableName} {whereSql} ";

            if (orderBy != null && orderBy.Length > 0)
            {
                var orderSql = string.Join(", ", orderBy);
                sql += $" ORDER BY {orderSql} ";
            }

            if(skip.HasValue || take.HasValue)
            {
                int s = skip ?? 0;
                int t = take ?? 100;
                // SQL Server pagination syntax
                sql += $" OFFSET {s} ROWS FETCH NEXT {t} ROWS ONLY ";
            }

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var res = await Conn.QueryAsync<T>(cmd).ConfigureAwait(false);

            return res;
        }

        public async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            var key = SqlServerHelpers<T>.TableProps.Where(o => o.IsKey).FirstOrDefault();

            var setCols = SqlServerHelpers<T>.TableProps.Where(o => !o.IsKey && !o.IsIdentity).ToArray();

            if (setCols.Count() == 0) return;

            var setPart = string.Join(", ", setCols.Select(c => $"{c.Col} = @{c.Col}"));
            string sql = $"UPDATE {TableName} SET {setPart} WHERE {key.Col} = @{key.Col}";

            var dp = new DynamicParameters();

            foreach (var (Prop, Col, IsKey, IsIdentity) in setCols)
            {
                var val = Prop.GetValue(entity);
                dp.Add($"@{Col}", val);
            }

            // Add key parameter
            var keyVal = key.Prop.GetValue(entity);
            dp.Add($"@{key.Col}", keyVal);

            var cmd = new CommandDefinition(sql, dp, Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
